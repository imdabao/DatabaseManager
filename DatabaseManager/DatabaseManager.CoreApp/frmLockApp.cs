﻿using DatabaseInterpreter.Utility;
using DatabaseManager.Core;
using DatabaseManager.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace DatabaseManager
{
    public partial class frmLockApp : Form
    {
        private bool confirmClose = false;

        public frmLockApp()
        {
            InitializeComponent();
        }

        private void frmLockApp_Load(object sender, EventArgs e)
        {
            string password = SettingManager.Setting.LockPassword;

            if(!string.IsNullOrEmpty(password))
            {
                this.txtPassword.Text = AesHelper.Decrypt(password);
            }
        }

        private void frmLockApp_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!this.confirmClose)
            {
                MessageBox.Show("Please enter password to unlock!");
                e.Cancel = true;
            }
        }

        private void btnLock_Click(object sender, EventArgs e)
        {
            string password = this.txtPassword.Text.Trim();

            bool isLock = this.btnLock.Text == "Lock";

            if(isLock)
            {
                if (string.IsNullOrEmpty(password))
                {
                    MessageBox.Show("Please enter the lock password!");
                    return;
                }

                DataStore.LockPassword = password;

                this.txtPassword.Text = "";

                this.btnLock.Text = "Unlock";
                this.lblMessage.Visible = true;
            }
            else
            {
                if (string.IsNullOrEmpty(password))
                {
                    MessageBox.Show("Please enter the lock password!");
                    return;
                }
                else if(password != DataStore.LockPassword)
                {
                    MessageBox.Show("The lock password is incorrect!");
                    return;
                }

                DataStore.LockPassword = null;

                this.confirmClose = true;

                this.Close();
            }
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Are you sure to exit the application?", "Confirm", MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {
                this.confirmClose = true;

                Application.Exit();
            }
        }       
    }
}
