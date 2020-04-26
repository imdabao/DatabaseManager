﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using DatabaseManager.Model;
using DatabaseInterpreter.Model;
using DatabaseManager.Helper;
using DatabaseConverter.Core;
using DatabaseConverter.Model;
using DatabaseManager.Data;
using DatabaseInterpreter.Core;

namespace DatabaseManager.Controls
{
    public delegate void QueryEditorInfoMessageHandler(string information);

    public partial class UC_QueryEditor : UserControl
    {
        private Regex nameRegex = new Regex(@"\b(^[_a-zA-Z][ _0-9a-zA-Z]+$)\b");
        private SchemaInfo schemaInfo;
        private IEnumerable<string> keywords;
        private IEnumerable<FunctionSpecification> builtinFunctions;
        private List<SqlWord> allWords;
        private bool intellisenseSetuped;
        private bool enableIntellisense;
        private bool isPasting = false;
        private List<string> dbOwners;
        private const int WordListMinWidth = 160;

        public DatabaseType DatabaseType { get; set; }
        public DbInterpreter DbInterpreter { get; set; }
        public event EventHandler SetupIntellisenseRequired;

        public QueryEditorInfoMessageHandler OnQueryEditorInfoMessage;
        public UC_QueryEditor()
        {
            InitializeComponent();

            this.lvWords.MouseWheel += LvWords_MouseWheel;
            this.panelWords.VerticalScroll.Enabled = true;
            this.panelWords.VerticalScroll.Visible = true;
        }

        private void LvWords_MouseWheel(object sender, MouseEventArgs e)
        {
            if (this.panelWords.Visible && this.txtToolTip.Visible)
            {
                this.txtToolTip.Visible = false;
            }
        }

        public RichTextBox Editor => this.txtEditor;

        public void SetupIntellisence()
        {
            this.intellisenseSetuped = true;
            this.enableIntellisense = true;
            this.keywords = KeywordManager.GetKeywords(this.DatabaseType);
            this.builtinFunctions = FunctionManager.GetFunctionSpecifications(this.DatabaseType);
            this.schemaInfo = DataStore.GetSchemaInfo(this.DatabaseType);
            this.allWords = SqlWordFinder.FindWords(this.DatabaseType, "");
            this.dbOwners = this.allWords.Where(item => item.Type == SqlWordTokenType.Owner).Select(item => item.Text).ToList();
        }

        private void tsmiCopy_Click(object sender, EventArgs e)
        {
            Clipboard.SetDataObject(this.txtEditor.SelectedText);
        }

        private void tsmiPaste_Click(object sender, EventArgs e)
        {
            this.txtEditor.Paste();
        }

        private void txtEditor_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                this.tsmiCopy.Enabled = this.txtEditor.SelectionLength > 0;
                this.tsmiDisableIntellisense.Text = $"{(this.enableIntellisense ? "Disable" : "Enable")} Intellisense";
                this.tsmiUpdateIntellisense.Visible = this.enableIntellisense;
                this.editorContexMenu.Show(this.txtEditor, e.Location);
            }
        }

        private void txtEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                if (FormEventCenter.OnRunScripts != null)
                {
                    FormEventCenter.OnRunScripts();
                }
            }
            else if (e.Control && e.KeyCode == Keys.V)
            {
                this.isPasting = true;
                return;
            }

            if (!this.enableIntellisense)
            {
                return;
            }

            if (e.KeyCode == Keys.Down)
            {
                if (this.panelWords.Visible && !this.lvWords.Focused)
                {
                    this.lvWords.Focus();

                    if (this.lvWords.Items.Count > 0)
                    {
                        this.lvWords.Items[0].Selected = true;
                    }

                    e.SuppressKeyPress = true;
                }
            }
        }

        private void txtEditor_KeyUp(object sender, KeyEventArgs e)
        {
            this.ShowCurrentPosition();

            if (!this.enableIntellisense)
            {
                return;
            }

            if (this.isPasting)
            {
                return;
            }

            try
            {
                this.HandleKeyUpFoIntellisense(e);
            }
            catch (Exception ex)
            {
            }
        }

        private void HandleKeyUpFoIntellisense(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                this.ClearStypeForSpace();
                this.SetWordListViewVisible(false);
            }

            SqlWordToken token = this.GetLastWordToken();

            if (token == null || token.Text == null || token.Type != SqlWordTokenType.None)
            {
                this.SetWordListViewVisible(false);

                if (token != null)
                {
                    if (token.Type != SqlWordTokenType.String && token.Text.Contains("'"))
                    {
                        this.ClearStyle(token);
                    }
                }

                return;
            }

            if (this.panelWords.Visible)
            {
                if (this.lvWords.Tag is SqlWord word)
                {
                    if (word.Type == SqlWordTokenType.Table)
                    {
                        string columnName = null;

                        int index = this.txtEditor.SelectionStart;
                        char c = this.txtEditor.Text[index - 1];

                        if (c != '.')
                        {
                            columnName = token.Text;
                        }

                        this.ShowTableColumns(word.Text, columnName);
                    }
                    else if (word.Type == SqlWordTokenType.Owner)
                    {
                        this.ShowDbObjects(token.Text);
                    }

                    return;
                }
            }

            if (e.KeyData == Keys.OemPeriod)
            {
                SqlWord word = this.FindWord(token.Text);

                if (word.Type == SqlWordTokenType.Table)
                {
                    this.ShowTableColumns(word.Text);
                    this.lvWords.Tag = word;
                }
                else if (word.Type == SqlWordTokenType.Owner)
                {
                    this.ShowDbObjects(null, word.Text);
                    this.lvWords.Tag = word;
                }
            }
            else if (e.KeyCode == Keys.Back)
            {
                if (this.panelWords.Visible)
                {
                    if (!this.IsMatchWord(token.Text) || this.txtEditor.Text.Length == 0)
                    {
                        this.SetWordListViewVisible(false);
                    }
                    else
                    {
                        this.ShowWordListByToken(token);
                    }
                }

                if(this.DbInterpreter.CommentString.Contains(token.Text))
                {
                    int start = this.txtEditor.SelectionStart;
                    int lineIndex = this.txtEditor.GetLineFromCharIndex(start);
                    int stop =  this.txtEditor.GetFirstCharIndexFromLine(lineIndex) + this.txtEditor.Lines[lineIndex].Length - 1;
                    RichTextBoxHelper.Highlighting(this.txtEditor, this.DatabaseType, true, start, stop);
                }
            }
            else if (e.KeyValue < 48 || (e.KeyValue >= 58 && e.KeyValue <= 64) || (e.KeyValue >= 91 && e.KeyValue <= 96) || e.KeyValue > 122)
            {
                this.SetWordListViewVisible(false);
            }
            else
            {
                this.ShowWordListByToken(token);
            }
        }

        private void ShowWordListByToken(SqlWordToken token)
        {
            if (string.IsNullOrEmpty(token.Text))
            {
                this.SetWordListViewVisible(false);

                return;
            }

            SqlWordTokenType type = this.DetectTypeByWord(token.Text);

            var words = SqlWordFinder.FindWords(this.DatabaseType, token.Text, type);

            this.ShowWordList(words);
        }

        private void ShowTableColumns(string tableName, string columnName = null)
        {
            IEnumerable<SqlWord> columns = SqlWordFinder.FindWords(this.DatabaseType, columnName, SqlWordTokenType.TableColumn, tableName);

            this.ShowWordList(columns);
        }

        private void ShowDbObjects(string search, string owner = null)
        {
            IEnumerable<SqlWord> words = SqlWordFinder.FindWords(this.DatabaseType, search, SqlWordTokenType.Table | SqlWordTokenType.View | SqlWordTokenType.Function, owner);

            if (!string.IsNullOrEmpty(search))
            {
                List<SqlWord> sortedWords = new List<SqlWord>();

                sortedWords.AddRange(words.Where(item => item.Text.StartsWith(search, StringComparison.OrdinalIgnoreCase)));
                sortedWords.AddRange(words.Where(item => !item.Text.StartsWith(search, StringComparison.OrdinalIgnoreCase)));

                this.ShowWordList(sortedWords);
            }
            else
            {
                this.ShowWordList(words);
            }
        }

        private void ShowWordList(IEnumerable<SqlWord> words)
        {
            if (words.Count() > 0)
            {
                this.lvWords.Items.Clear();

                foreach (SqlWord sw in words)
                {
                    ListViewItem item = new ListViewItem();

                    switch (sw.Type)
                    {
                        case SqlWordTokenType.Keyword:
                            item.ImageIndex = 0;
                            break;
                        case SqlWordTokenType.BuiltinFunction:
                            item.ImageIndex = 1;
                            break;
                        case SqlWordTokenType.Table:
                            item.ImageIndex = 2;
                            break;
                        case SqlWordTokenType.View:
                            item.ImageIndex = 3;
                            break;
                        case SqlWordTokenType.TableColumn:
                            item.ImageIndex = 4;
                            break;
                        case SqlWordTokenType.Owner:
                            item.ImageIndex = 5;
                            break;
                    }

                    item.SubItems.Add(sw.Text);
                    item.SubItems[1].Tag = sw.Type;
                    item.Tag = sw.Source;

                    this.lvWords.Items.Add(item);
                }

                string longestText = words.OrderByDescending(item => item.Text.Length).FirstOrDefault().Text;

                int width = this.MeasureTextWidth(this.lvWords, longestText);

                this.lvWords.Columns[1].Width = width + 20;

                int totalWidth = this.lvWords.Columns.Cast<ColumnHeader>().Sum(item => item.Width) + 50;

                this.panelWords.Width = totalWidth < WordListMinWidth ? WordListMinWidth : totalWidth;

                this.SetWordListPanelPostition();

                this.SetWordListViewVisible(true);
            }
            else
            {
                this.SetWordListViewVisible(false);
            }
        }

        private void SetWordListPanelPostition()
        {
            Point point = this.txtEditor.GetPositionFromCharIndex(txtEditor.SelectionStart);
            point.Y += (int)Math.Ceiling(this.txtEditor.Font.GetHeight()) + 2;
            point.X += 2;

            this.panelWords.Location = point;
        }

        private void ClearStyle(SqlWordToken token)
        {
            this.txtEditor.Select(token.StartIndex, token.StopIndex - token.StartIndex + 1);
            this.txtEditor.SelectionColor = Color.Black;
            this.txtEditor.SelectionStart = token.StopIndex + 1;
            this.txtEditor.SelectionLength = 0;
        }

        private void ClearStypeForSpace()
        {
            int start = this.txtEditor.SelectionStart;
            this.txtEditor.Select(start - 1, 1);
            this.txtEditor.SelectionColor = Color.Black;
            this.txtEditor.SelectionStart = start;
            this.txtEditor.SelectionLength = 0;
        }

        private SqlWordTokenType DetectTypeByWord(string word)
        {
            switch (word.ToUpper())
            {
                case "FROM":
                    return SqlWordTokenType.Table | SqlWordTokenType.View;
            }

            return SqlWordTokenType.None;
        }

        private bool IsMatchWord(string word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return false;
            }

            var words = SqlWordFinder.FindWords(this.DatabaseType, word);

            return words.Count > 0;
        }

        private void SetWordListViewVisible(bool visible)
        {
            if (visible)
            {
                this.panelWords.BringToFront();
                this.panelWords.Show();
            }
            else
            {
                this.txtToolTip.Hide();
                this.panelWords.Hide();
                this.lvWords.Tag = null;
            }
        }

        private SqlWordToken GetLastWordToken(bool noAction = false, bool isInsert = false)
        {
            SqlWordToken token = null;

            int currentIndex = this.txtEditor.SelectionStart;
            int lineIndex = this.txtEditor.GetLineFromCharIndex(currentIndex);
            int lineFirstCharIndex = this.txtEditor.GetFirstCharIndexOfCurrentLine();

            int index = currentIndex - 1;

            if (index < 0 || index > this.txtEditor.Text.Length - 1)
            {
                return token;
            }

            token = new SqlWordToken();

            bool isDot = false;

            if (this.txtEditor.Text[index] == '.')
            {
                isDot = true;

                if (isInsert)
                {
                    token.StartIndex = token.StopIndex = this.txtEditor.SelectionStart;

                    return token;
                }

                index = index - 1;
            }

            token.StopIndex = index;

            string lineBefore = this.txtEditor.Text.Substring(lineFirstCharIndex, currentIndex - lineFirstCharIndex);

            bool isComment = false;

            if (lineBefore.Contains(this.DbInterpreter.CommentString))
            {
                isComment = true;
            }

            string word = "";

            if (!isComment)
            {
                List<char> chars = new List<char>();

                string delimeterPattern = @"[ ,\.\r\n=]";

                int i = -1;

                bool exited = false;
                for (i = index; i >= 0; i--)
                {
                    char c = this.txtEditor.Text[i];

                    if (!Regex.IsMatch(c.ToString(), delimeterPattern))
                    {
                        chars.Add(c);

                        if (c == '\'')
                        {
                            break;
                        }
                        else if (c == '(')
                        {
                            if (chars.Count > 1)
                            {
                                chars.RemoveAt(chars.Count - 1);
                                i++;
                            }

                            break;
                        }
                    }
                    else
                    {
                        exited = true;
                        break;
                    }
                }

                if (i == -1)
                {
                    i = 0;
                }

                chars.Reverse();

                word = string.Join("", chars);

                token.Text = word;

                token.StartIndex = i + (exited ? 1 : 0);

                if (word.Contains("'"))
                {
                    int singQuotationCount = lineBefore.Count(item => item == '\'');

                    bool isQuotationPaired = singQuotationCount % 2 == 0;

                    if (isQuotationPaired && word.StartsWith("'"))
                    {
                        List<char> afterChars = new List<char>();
                        for (int j = currentIndex; j < this.txtEditor.Text.Length; j++)
                        {
                            char c = this.txtEditor.Text[j];
                            if (Regex.IsMatch(c.ToString(), delimeterPattern))
                            {
                                break;
                            }
                            else
                            {
                                afterChars.Add(c);
                            }
                        }

                        string afterWord = string.Join("", afterChars);

                        if (afterWord.EndsWith("'") || (word == "'" && afterChars.Count == 0))
                        {
                            token.Type = SqlWordTokenType.String;
                        }
                        else
                        {
                            token.StartIndex++;
                            token.Text = token.Text.Substring(1);
                        }
                    }
                    else if (!isQuotationPaired || (isQuotationPaired && word.EndsWith("'")))
                    {
                        token.Type = SqlWordTokenType.String;
                    }

                    if (token.Type == SqlWordTokenType.String)
                    {
                        this.SetWordColor(token);
                        return token;
                    }
                }
            }
            else
            {
                int firstIndexOfComment = lineBefore.IndexOf(this.DbInterpreter.CommentString);

                token.StartIndex = firstIndexOfComment;
                token.StopIndex = lineFirstCharIndex + this.txtEditor.Lines[lineIndex].Length - 1;
            }

            if (!noAction)
            {
                if (this.keywords.Any(item => item.ToUpper() == word.ToUpper()))
                {
                    token.Type = SqlWordTokenType.Keyword;

                    this.SetWordColor(token);

                }
                else if (this.builtinFunctions.Any(item => item.Name.ToUpper() == word.ToUpper()))
                {
                    token.Type = SqlWordTokenType.BuiltinFunction;

                    this.SetWordColor(token);
                }
                else if (isComment)
                {
                    token.Type = SqlWordTokenType.Comment;
                    this.SetWordColor(token, true);
                }
                else
                {
                    if (!isDot)
                    {
                        this.ClearStyle(token);
                    }
                }
            }

            return token;
        }

        private SqlWord FindWord(string text)
        {
            text = this.TrimQuotationChars(text);

            SqlWord word = null;

            if (this.dbOwners.Count > 0 && this.dbOwners.Any(item => text.ToUpper() == item.ToUpper()))
            {
                word = new SqlWord() { Type = SqlWordTokenType.Owner, Text = text };

                return word;
            }

            word = this.allWords.FirstOrDefault(item => item.Text.ToUpper() == text.ToUpper()
                                && (item.Type == SqlWordTokenType.Table || item.Type == SqlWordTokenType.View));

            if (word != null)
            {
                return word;
            }
            else
            {
                word = new SqlWord() { Text = text };
            }

            Regex regex = new Regex($@"([{this.DbInterpreter.QuotationLeftChar}]?\b([_a-zA-Z][ _0-9a-zA-Z]+)\b[{this.DbInterpreter.QuotationRightChar}]?)?[\s\n\r]+\b({text})\b", RegexOptions.IgnoreCase);

            var matches = regex.Matches(this.txtEditor.Text);

            string name = "";
            foreach (Match match in matches)
            {
                if (match.Value.Trim().ToUpper() != text.ToUpper())
                {
                    string[] items = match.Value.Split(' ');

                    var reversedItems = items.Reverse().ToArray();

                    for (int i = 0; i < reversedItems.Length; i++)
                    {
                        if (reversedItems[i] == text)
                        {
                            if (i + 1 < reversedItems.Length && !this.keywords.Any(item => item.ToUpper() == reversedItems[i + 1].ToUpper()))
                            {
                                name = this.TrimQuotationChars(reversedItems[i + 1]);
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(name))
            {
                name = text;
            }

            if (this.schemaInfo.Tables.Any(item => item.Name.ToUpper() == name.ToUpper()))
            {
                word.Text = name;
                word.Type = SqlWordTokenType.Table;
            }

            return word;
        }

        private string TrimQuotationChars(string value)
        {
            return value.Trim(this.DbInterpreter.QuotationLeftChar, this.DbInterpreter.QuotationRightChar);
        }

        private void SetWordColor(SqlWordToken token, bool keepCurrentPos = false)
        {
            Color color = Color.Black;

            if (token.Type == SqlWordTokenType.Keyword)
            {
                color = Color.Blue;
            }
            else if (token.Type == SqlWordTokenType.BuiltinFunction)
            {
                color = ColorTranslator.FromHtml("#FF00FF");
            }
            else if (token.Type == SqlWordTokenType.String)
            {
                color = Color.Red;
            }
            else if (token.Type == SqlWordTokenType.Comment)
            {
                color = ColorTranslator.FromHtml("#008000");
            }

            int start = this.txtEditor.SelectionStart;

            this.txtEditor.Select(token.StartIndex, token.StopIndex - token.StartIndex + 1);
            this.txtEditor.SelectionBackColor = this.txtEditor.BackColor;
            this.txtEditor.SelectionColor = color;
            this.txtEditor.SelectionStart = keepCurrentPos ? start : token.StopIndex + 1;
            this.txtEditor.SelectionLength = 0;
        }

        private void InsertSelectedWord()
        {
            SqlWordToken token = this.GetLastWordToken(true, true);

            ListViewItem item = this.lvWords.SelectedItems[0];
            object tag = item.Tag;

            string selectedWord = item.SubItems[1].Text;

            this.txtEditor.Select(token.StartIndex, token.StopIndex - token.StartIndex + 1);

            string quotationValue = selectedWord;

            if (!(tag is FunctionSpecification))
            {
                quotationValue = this.DbInterpreter.GetQuotedString(selectedWord);
            }

            this.txtEditor.SelectedText = quotationValue;

            this.SetWordListViewVisible(false);

            this.txtEditor.SelectionStart = this.txtEditor.SelectionStart;
            this.txtEditor.Focus();
        }

        private void ShowCurrentPosition()
        {
            string message = "";

            if (this.txtEditor.SelectionStart >= 0)
            {
                int lineIndex = this.txtEditor.GetLineFromCharIndex(this.txtEditor.SelectionStart);
                int column = this.txtEditor.SelectionStart - this.txtEditor.GetFirstCharIndexOfCurrentLine() + 1;

                message = $"Line:{lineIndex + 1}  Column:{column} Index:{this.txtEditor.SelectionStart}";
            }
            else
            {
                message = "";
            }

            if (this.OnQueryEditorInfoMessage != null)
            {
                this.OnQueryEditorInfoMessage(message);
            }
        }

        private void lvWords_DoubleClick(object sender, EventArgs e)
        {
            if (this.lvWords.SelectedItems.Count > 0)
            {
                this.InsertSelectedWord();
            }
        }

        private void lvWords_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.InsertSelectedWord();
            }
            else if (!(e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                if (this.panelWords.Visible)
                {
                    this.panelWords.Visible = false;
                    this.txtEditor.SelectionStart = this.txtEditor.SelectionStart;
                    this.txtEditor.Focus();
                }
            }
        }

        private void tsmiDisableIntellisense_Click(object sender, EventArgs e)
        {
            this.enableIntellisense = !this.enableIntellisense;

            if (this.enableIntellisense && !this.intellisenseSetuped)
            {
                if (this.SetupIntellisenseRequired != null)
                {
                    this.SetupIntellisenseRequired(this, null);
                }
            }
        }

        private void txtEditor_SelectionChanged(object sender, EventArgs e)
        {
            this.txtEditor.SelectionFont = this.txtEditor.Font;

            if (this.isPasting)
            {
                this.isPasting = false;

                RichTextBoxHelper.Highlighting(this.txtEditor, this.DatabaseType);
            }
        }

        private void lvWords_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.txtToolTip.Visible = false;

            if (this.lvWords.SelectedItems.Count > 0)
            {
                ListViewItem item = this.lvWords.SelectedItems[0];

                object source = item.Tag;
                string tooltip = null;

                if (source is FunctionSpecification funcSpec)
                {
                    tooltip = $"{funcSpec.Name}({funcSpec.Args})";
                }
                else if (source is TableColumn column)
                {
                    tooltip = $"{column.Name}({this.DbInterpreter.ParseDataType(column)})";
                }

                if (!string.IsNullOrEmpty(tooltip))
                {
                    this.ShowTooltip(tooltip, item);
                }
            }
        }

        private void ShowTooltip(string text, ListViewItem item)
        {
            this.txtToolTip.Text = text;

            this.txtToolTip.Location = new Point(this.panelWords.Location.X + this.panelWords.Width, this.panelWords.Location.Y + item.Position.Y);

            this.txtToolTip.Width = this.MeasureTextWidth(this.txtToolTip, text);

            this.txtToolTip.Visible = true;
        }

        private int MeasureTextWidth(Control control, string text)
        {
            using (Graphics g = this.CreateGraphics())
            {
                return (int)Math.Ceiling(g.MeasureString(text, control.Font).Width);
            }
        }

        private void tsmiUpdateIntellisense_Click(object sender, EventArgs e)
        {
            if (this.SetupIntellisenseRequired != null)
            {
                this.SetupIntellisenseRequired(this, null);
            }
        }

        private void txtEditor_MouseClick(object sender, MouseEventArgs e)
        {
            this.HandleMouseDownClick(e);
        }

        private void txtEditor_MouseDown(object sender, MouseEventArgs e)
        {
            this.HandleMouseDownClick(e);
        }

        private void HandleMouseDownClick(MouseEventArgs e)
        {
            this.ShowCurrentPosition();

            this.isPasting = false;

            if (!this.enableIntellisense)
            {
                return;
            }

            this.txtToolTip.Visible = false;

            if (this.panelWords.Visible && !this.panelWords.Bounds.Contains(e.Location))
            {
                this.panelWords.Visible = false;
                this.lvWords.Items.Clear();
                this.lvWords.Tag = null;
            }
        }
    }
}
