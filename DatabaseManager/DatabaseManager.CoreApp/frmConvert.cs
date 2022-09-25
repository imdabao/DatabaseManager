﻿using DatabaseConverter.Core;
using DatabaseConverter.Model;
using DatabaseInterpreter.Core;
using DatabaseInterpreter.Model;
using DatabaseInterpreter.Utility;
using DatabaseManager.Controls;
using DatabaseManager.Helper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DatabaseManager
{
    public partial class frmConvert : Form, IObserver<FeedbackInfo>
    {
        private const string DONE = "Convert finished";
        private DatabaseType sourceDatabaseType;
        private ConnectionInfo sourceDbConnectionInfo;
        private ConnectionInfo targetDbConnectionInfo;
        private DbConverter dbConverter = null;
        private bool useSourceConnector = true;
        private List<SchemaMappingInfo> schemaMappings = new List<SchemaMappingInfo>();
        public frmConvert()
        {
            InitializeComponent();
        }

        public frmConvert(DatabaseType sourceDatabaseType, ConnectionInfo sourceConnectionInfo)
        {
            InitializeComponent();

            this.sourceDatabaseType = sourceDatabaseType;
            this.sourceDbConnectionInfo = sourceConnectionInfo;
            this.useSourceConnector = false;
            
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            this.Init();
        }

        private void Init()
        {
            TextBox.CheckForIllegalCrossThreadCalls = false;
            CheckBox.CheckForIllegalCrossThreadCalls = false;

            if (!this.useSourceConnector)
            {
                int increaseHeight = this.sourceDbProfile.Height;
                this.sourceDbProfile.Visible = false;
                this.btnFetch.Height = this.targetDbProfile.ClientHeight;
                this.targetDbProfile.Top -= increaseHeight;
                this.tvDbObjects.Top -= increaseHeight;
                this.gbOption.Top -= increaseHeight;
                this.tvDbObjects.Height += increaseHeight;
                this.gbOption.Height += increaseHeight;
            }
        }

        private void btnFetch_Click(object sender, EventArgs e)
        {
            this.Invoke(new Action(this.Fetch));
        }

        private async void Fetch()
        {
            DatabaseType dbType;

            if (this.useSourceConnector)
            {
                dbType = this.sourceDbProfile.DatabaseType;

                if (!this.sourceDbProfile.IsDbTypeSelected())
                {
                    MessageBox.Show("Please select a source database type.");
                    return;
                }

                if (!this.sourceDbProfile.IsProfileSelected())
                {
                    MessageBox.Show("Please select a source database profile.");
                    return;
                }

                if (!this.sourceDbConnectionInfo.IntegratedSecurity && string.IsNullOrEmpty(this.sourceDbConnectionInfo.Password))
                {
                    MessageBox.Show("Please specify password for the source database.");
                    this.sourceDbProfile.ConfigConnection(true);
                    return;
                }
            }
            else
            {
                dbType = this.sourceDatabaseType;
            }

            this.btnFetch.Text = "...";

            try
            {
                DatabaseObjectType excludeObjType = (this.sourceDatabaseType != DatabaseType.SqlServer || this.targetDbProfile.DatabaseType != DatabaseType.SqlServer) ? DatabaseObjectType.UserDefinedType : DatabaseObjectType.None;

                await this.tvDbObjects.LoadTree(dbType, this.sourceDbConnectionInfo, excludeObjType);
                this.btnExecute.Enabled = true;
            }
            catch (Exception ex)
            {
                this.tvDbObjects.ClearNodes();

                string message = ExceptionHelper.GetExceptionDetails(ex);

                LogHelper.LogError(message);

                MessageBox.Show("Error:" + message);
            }

            this.btnFetch.Text = "Fetch";
        }

        private async void btnExecute_Click(object sender, EventArgs e)
        {
            this.txtMessage.ForeColor = Color.Black;
            this.txtMessage.Text = "";

            await Task.Run(() => this.Convert());
        }

        private bool ValidateSource(SchemaInfo schemaInfo)
        {
            if (!this.tvDbObjects.HasDbObjectNodeSelected())
            {
                MessageBox.Show("Please select objects from tree.");
                return false;
            }

            if (this.sourceDbConnectionInfo == null)
            {
                MessageBox.Show("Source connection is null.");
                return false;
            }

            return true;
        }

        private void SetGenerateScriptOption(params DbInterpreterOption[] options)
        {
            if (options != null)
            {
                string outputFolder = this.txtOutputFolder.Text.Trim();
                foreach (DbInterpreterOption option in options)
                {
                    if (Directory.Exists(outputFolder))
                    {
                        option.ScriptOutputFolder = outputFolder;
                    }
                }
            }
        }

        private GenerateScriptMode GetGenerateScriptMode()
        {
            GenerateScriptMode scriptMode = GenerateScriptMode.None;
            if (this.chkScriptSchema.Checked)
            {
                scriptMode = scriptMode | GenerateScriptMode.Schema;
            }
            if (this.chkScriptData.Checked)
            {
                scriptMode = scriptMode | GenerateScriptMode.Data;
            }

            return scriptMode;
        }

        private async Task Convert()
        {
            SchemaInfo schemaInfo = this.tvDbObjects.GetSchemaInfo();

            if (!this.ValidateSource(schemaInfo))
            {
                return;
            }

            if (this.targetDbConnectionInfo == null)
            {
                MessageBox.Show("Target connection info is null.");
                return;
            }

            if (this.sourceDbConnectionInfo.Server == this.targetDbConnectionInfo.Server && this.sourceDbConnectionInfo.Database == this.targetDbConnectionInfo.Database)
            {
                MessageBox.Show("Source database cannot be equal to the target database.");
                return;
            }

            DatabaseType sourceDbType = this.useSourceConnector ? this.sourceDbProfile.DatabaseType : this.sourceDatabaseType;
            DatabaseType targetDbType = this.targetDbProfile.DatabaseType;

            DbInterpreterOption sourceScriptOption = new DbInterpreterOption() { ScriptOutputMode = GenerateScriptOutputMode.None, SortObjectsByReference = true, GetTableAllObjects = true, ThrowExceptionWhenErrorOccurs = false };
            DbInterpreterOption targetScriptOption = new DbInterpreterOption() { ScriptOutputMode = GenerateScriptOutputMode.WriteToString, ThrowExceptionWhenErrorOccurs = false };

            this.SetGenerateScriptOption(sourceScriptOption, targetScriptOption);

            if (this.chkGenerateSourceScripts.Checked)
            {
                sourceScriptOption.ScriptOutputMode = sourceScriptOption.ScriptOutputMode | GenerateScriptOutputMode.WriteToFile;
            }

            if (this.chkOutputScripts.Checked)
            {
                targetScriptOption.ScriptOutputMode = targetScriptOption.ScriptOutputMode | GenerateScriptOutputMode.WriteToFile;
            }

            if (this.chkTreatBytesAsNull.Checked)
            {
                sourceScriptOption.TreatBytesAsNullForReading = true;
                targetScriptOption.TreatBytesAsNullForExecuting = true;
            }

            targetScriptOption.TableScriptsGenerateOption.GenerateIdentity = this.chkGenerateIdentity.Checked;
            targetScriptOption.TableScriptsGenerateOption.GenerateComment = this.chkGenerateComment.Checked;

            GenerateScriptMode scriptMode = this.GetGenerateScriptMode();

            if (scriptMode == GenerateScriptMode.None)
            {
                MessageBox.Show("Please specify the script mode.");
                return;
            }

            DbConveterInfo source = new DbConveterInfo() { DbInterpreter = DbInterpreterHelper.GetDbInterpreter(sourceDbType, this.sourceDbConnectionInfo, sourceScriptOption) };
            DbConveterInfo target = new DbConveterInfo() { DbInterpreter = DbInterpreterHelper.GetDbInterpreter(targetDbType, this.targetDbConnectionInfo, targetScriptOption) };

            try
            {
                using (this.dbConverter = new DbConverter(source, target))
                {
                    this.dbConverter.Option.GenerateScriptMode = scriptMode;
                    this.dbConverter.Option.BulkCopy = this.chkBulkCopy.Checked;
                    this.dbConverter.Option.ExecuteScriptOnTargetServer = this.chkExecuteOnTarget.Checked;
                    this.dbConverter.Option.UseTransaction = this.chkUseTransaction.Checked;
                    this.dbConverter.Option.ContinueWhenErrorOccurs = this.chkContinueWhenErrorOccurs.Checked;
                    this.dbConverter.Option.ConvertComputeColumnExpression = this.chkComputeColumn.Checked;
                    this.dbConverter.Option.OnlyCommentComputeColumnExpressionInScript = this.chkOnlyCommentComputeExpression.Checked;
                    this.dbConverter.Option.SplitScriptsToExecute = true;                   

                    this.dbConverter.Option.SchemaMappings = this.schemaMappings;

                    if (sourceDbType == DatabaseType.MySql)
                    {
                        source.DbInterpreter.Option.InQueryItemLimitCount = 2000;
                    }

                    if (targetDbType == DatabaseType.MySql)
                    {
                        target.DbInterpreter.Option.RemoveEmoji = true;
                    }

                    this.dbConverter.Subscribe(this);

                    this.SetExecuteButtonEnabled(false);

                    DbConverterResult result = await this.dbConverter.Convert(schemaInfo);

                    this.SetExecuteButtonEnabled(true);

                    if (result.InfoType == DbConverterResultInfoType.Information)
                    {
                        if (!this.dbConverter.CancelRequested)
                        {
                            this.txtMessage.AppendText(Environment.NewLine + DONE);
                            MessageBox.Show(result.Message, "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("Task has been canceled.");
                        }
                    }
                    else if (result.InfoType == DbConverterResultInfoType.Warnning)
                    {
                        MessageBox.Show(result.Message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else if (result.InfoType == DbConverterResultInfoType.Error)
                    {
                        MessageBox.Show(result.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                if (this.dbConverter != null)
                {
                    this.dbConverter = null;
                }

                this.HandleException(ex);
            }
        }

        private void SetExecuteButtonEnabled(bool enable)
        {
            this.btnExecute.Enabled = enable;
            this.btnCancel.Enabled = !enable;
        }

        private void HandleException(Exception ex)
        {
            string errMsg = ExceptionHelper.GetExceptionDetails(ex);

            LogHelper.LogError(errMsg);

            this.AppendMessage(errMsg, true);

            this.txtMessage.SelectionStart = this.txtMessage.TextLength;
            this.txtMessage.ScrollToCaret();

            this.btnExecute.Enabled = true;
            this.btnCancel.Enabled = false;

            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void Feedback(FeedbackInfo info)
        {
            this.Invoke(new Action(() =>
            {
                if (info.InfoType == FeedbackInfoType.Error)
                {
                    if (!info.IgnoreError)
                    {
                        if (this.chkExecuteOnTarget.Checked && !this.chkContinueWhenErrorOccurs.Checked)
                        {
                            if (this.dbConverter != null && this.dbConverter.IsBusy)
                            {
                                this.dbConverter.Cancle();
                            }

                            this.SetExecuteButtonEnabled(true);
                        }
                    }

                    this.AppendMessage(info.Message, true);
                }
                else
                {
                    this.AppendMessage(info.Message, false);
                }
            }));
        }

        private void AppendMessage(string message, bool isError = false)
        {
            RichTextBoxHelper.AppendMessage(this.txtMessage, message, isError);
        }

        private bool ConfirmCancel()
        {
            if (MessageBox.Show("Are you sure to abandon current task?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                return true;
            }
            return false;
        }

        private async void btnCancel_Click(object sender, EventArgs e)
        {
            await Task.Run(() =>
            {
                if (this.dbConverter != null && this.dbConverter.IsBusy)
                {
                    if (this.ConfirmCancel())
                    {
                        this.dbConverter.Cancle();

                        this.SetExecuteButtonEnabled(true);
                    }
                }
            });
        }

        #region IObserver<FeedbackInfo>
        void IObserver<FeedbackInfo>.OnCompleted()
        {
        }
        void IObserver<FeedbackInfo>.OnError(Exception error)
        {
        }
        void IObserver<FeedbackInfo>.OnNext(FeedbackInfo info)
        {
            this.Feedback(info);
        }
        #endregion    

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.dbConverter != null && this.dbConverter.IsBusy)
            {
                if (this.ConfirmCancel())
                {
                    this.dbConverter.Cancle();
                    e.Cancel = false;
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }

        private void btnCopyMessage_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(this.txtMessage.Text))
            {
                Clipboard.SetDataObject(this.txtMessage.Text);
                MessageBox.Show("The message has been copied to clipboard.");
            }
            else
            {
                MessageBox.Show("There's no message.");
            }
        }

        private void btnSaveMessage_Click(object sender, EventArgs e)
        {
            if (this.dlgSaveLog == null)
            {
                this.dlgSaveLog = new SaveFileDialog();
            }

            if (!string.IsNullOrEmpty(this.txtMessage.Text))
            {
                this.dlgSaveLog.Filter = "txt files|*.txt|all files|*.*";
                DialogResult dialogResult = this.dlgSaveLog.ShowDialog();
                if (dialogResult == DialogResult.OK)
                {
                    File.WriteAllLines(this.dlgSaveLog.FileName, this.txtMessage.Text.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
                    this.dlgSaveLog.Reset();
                }
            }
            else
            {
                MessageBox.Show("There's no message.");
            }
        }

        private void btnOutputFolder_Click(object sender, EventArgs e)
        {
            if (this.dlgOutputFolder == null)
            {
                this.dlgOutputFolder = new FolderBrowserDialog();
            }

            DialogResult result = this.dlgOutputFolder.ShowDialog();
            if (result == DialogResult.OK)
            {
                this.txtOutputFolder.Text = this.dlgOutputFolder.SelectedPath;
            }
        }

        private void sourceDbProfile_OnSelectedChanged(object sender, ConnectionInfo connectionInfo)
        {
            this.sourceDbConnectionInfo = connectionInfo;
            this.schemaMappings = new List<SchemaMappingInfo>();
        }

        private void targetDbProfile_OnSelectedChanged(object sender, ConnectionInfo connectionInfo)
        {
            this.targetDbConnectionInfo = connectionInfo;

            UC_DbConnectionProfile connectionProfile = sender as UC_DbConnectionProfile;

            if (connectionProfile.IsDbTypeSelected())
            {
                DatabaseType databaseType = connectionProfile.DatabaseType;

                this.btnSetSchemaMappings.Enabled = !(databaseType== DatabaseType.Oracle || databaseType == DatabaseType.MySql);
            }
            else
            {
                this.btnSetSchemaMappings.Enabled = false;
            }

            this.schemaMappings = new List<SchemaMappingInfo>();
        }

        private void txtMessage_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (this.txtMessage.SelectionLength > 0)
                {
                    this.contextMenuStrip1.Show(this.txtMessage, e.Location);
                }
            }
        }

        private void tsmiCopySelection_Click(object sender, EventArgs e)
        {
            Clipboard.SetDataObject(this.txtMessage.SelectedText);
        }

        private void chkComputeColumn_CheckedChanged(object sender, EventArgs e)
        {
            this.chkOnlyCommentComputeExpression.Enabled = !this.chkComputeColumn.Checked;

            if (this.chkComputeColumn.Checked)
            {
                this.chkOnlyCommentComputeExpression.Checked = false;
            }
        }

        private async void btnSetSchemaMappings_Click(object sender, EventArgs e)
        {
            DatabaseType sourceDbType = this.useSourceConnector ? this.sourceDbProfile.DatabaseType : this.sourceDatabaseType;
            DatabaseType targetDbType = this.targetDbProfile.DatabaseType;

            if(sourceDbType == DatabaseType.Unknown || targetDbType == DatabaseType.Unknown)
            {
                return;
            }

            if(this.sourceDbConnectionInfo == null || this.targetDbConnectionInfo == null)
            {
                return;
            }

            DbInterpreterOption option = new DbInterpreterOption() { };

            DbInterpreter sourceInterpreter = DbInterpreterHelper.GetDbInterpreter(sourceDbType, this.sourceDbConnectionInfo, option);
            DbInterpreter targetInterpreter = DbInterpreterHelper.GetDbInterpreter(targetDbType, this.targetDbConnectionInfo, option);

            List<DatabaseSchema> sourceSchemas = null; 
            List<DatabaseSchema> targetSchemas = null;

            try
            {
                sourceSchemas = await sourceInterpreter.GetDatabaseSchemasAsync();
                targetSchemas = await targetInterpreter.GetDatabaseSchemasAsync();
            }
            catch (Exception ex)
            {
                sourceSchemas = new List<DatabaseSchema>();
                targetSchemas = new List<DatabaseSchema>();
            }

            frmSchemaMapping form = new frmSchemaMapping() { 
                Mappings = this.schemaMappings, 
                SourceSchemas = sourceSchemas.Select(item => item.Name).ToList(), 
                TargetSchemas = targetSchemas.Select(item => item.Name).ToList() 
            };

            if (form.ShowDialog() == DialogResult.OK)
            {
                this.schemaMappings = form.Mappings;
            }
        }
    }
}
