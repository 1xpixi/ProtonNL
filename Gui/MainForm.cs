using System.Runtime.InteropServices;

namespace ProtonNL.Gui;

internal sealed class MainForm : Form
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

    private static readonly Color TreeBack = Color.FromArgb(28, 28, 32);
    private static readonly Color TreeText = Color.FromArgb(237, 237, 242);
    private static readonly Color TreeSelect = Color.FromArgb(50, 50, 56);
    private readonly HookClient _client = new();
    private readonly TreeView _tree = new();
    private readonly Button _connect = new();
    private readonly Button _refresh = new();
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _reconnect = new();
    private bool _connecting;
    private bool _loading;
    private string _fingerprint = "";

    public MainForm()
    {
        Text = "ProtonNL";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(400, 580);
        MinimumSize = new Size(360, 440);
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.FromArgb(237, 237, 242);
        Font = new Font("Segoe UI", 9.5f);
        ShowInTaskbar = true;
        DoubleBuffered = true;

        Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
        Location = new Point(Math.Max(16, wa.Right - Width - 28), wa.Top + 56);

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18, 16, 18, 14),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label title = new()
        {
            Text = "Free servers",
            Font = new Font("Segoe UI Semibold", 16f),
            AutoSize = true,
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 2)
        };

        Label subtitle = new()
        {
            Text = "Country → city → server. Double-click a server to connect.",
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 158),
            Margin = new Padding(0, 0, 0, 12)
        };

        Panel treeHost = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 28, 32),
            Padding = new Padding(8, 8, 4, 8),
            Margin = new Padding(0, 0, 0, 12)
        };

        _tree.Dock = DockStyle.Fill;
        _tree.BorderStyle = BorderStyle.None;
        _tree.BackColor = Color.FromArgb(28, 28, 32);
        _tree.ForeColor = Color.FromArgb(237, 237, 242);
        _tree.LineColor = Color.FromArgb(92, 92, 102);
        _tree.FullRowSelect = true;
        _tree.HideSelection = false;
        _tree.HotTracking = false;
        _tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        _tree.DrawNode += TreeDrawNode;
        _tree.ShowLines = true;
        _tree.ShowRootLines = true;
        _tree.ShowPlusMinus = true;
        _tree.Indent = 19;
        _tree.ItemHeight = 26;
        _tree.Margin = Padding.Empty;
        _tree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node == null)
                return;
            if (e.Node.Nodes.Count > 0)
                return;
            ConnectNode(e.Node);
        };
        _tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ConnectSelected();
            }
        };

        treeHost.Controls.Add(_tree);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        _connect.Text = "Connect";
        _connect.Size = new Size(148, 34);
        _connect.FlatStyle = FlatStyle.Flat;
        _connect.FlatAppearance.BorderSize = 0;
        _connect.BackColor = Color.FromArgb(98, 70, 230);
        _connect.ForeColor = Color.White;
        _connect.Font = new Font("Segoe UI Semibold", 9.5f);
        _connect.Cursor = Cursors.Hand;
        _connect.Margin = new Padding(0, 0, 8, 0);
        _connect.Click += (_, _) => ConnectSelected();
        AcceptButton = _connect;

        _refresh.Text = "Refresh";
        _refresh.Size = new Size(92, 34);
        _refresh.FlatStyle = FlatStyle.Flat;
        _refresh.FlatAppearance.BorderSize = 0;
        _refresh.BackColor = Color.FromArgb(42, 42, 48);
        _refresh.ForeColor = Color.FromArgb(230, 230, 235);
        _refresh.Cursor = Cursors.Hand;
        _refresh.Margin = Padding.Empty;
        _refresh.Click += async (_, _) => await LoadListAsync(force: true);

        buttons.Controls.Add(_connect);
        buttons.Controls.Add(_refresh);

        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.Height = 22;
        _status.ForeColor = Color.FromArgb(140, 140, 148);
        _status.Text = "Connecting to ProtonVPN…";
        _status.Margin = Padding.Empty;

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(treeHost, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(_status, 0, 4);
        Controls.Add(root);

        _reconnect.Interval = 4000;
        _reconnect.Tick += async (_, _) =>
        {
            if (_connecting || _loading)
                return;
            if (_client.Connected && _tree.Nodes.Count > 0)
                return;
            await LoadListAsync(force: false);
        };

        Shown += async (_, _) =>
        {
            await LoadListAsync(force: true);
            _reconnect.Start();
        };
        FormClosed += (_, _) =>
        {
            _reconnect.Stop();
            _client.Dispose();
        };
        HandleCreated += (_, _) => SetWindowTheme(_tree.Handle, "", "");
        _tree.HandleCreated += (_, _) => SetWindowTheme(_tree.Handle, "", "");
    }

    private void TreeDrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node == null)
            return;

        bool selected = (e.State & TreeNodeStates.Selected) != 0;
        Rectangle row = new(e.Bounds.X, e.Bounds.Y, Math.Max(0, _tree.ClientSize.Width - e.Bounds.X), e.Bounds.Height);
        using SolidBrush bg = new(selected ? TreeSelect : TreeBack);
        e.Graphics.FillRectangle(bg, row);

        TextRenderer.DrawText(
            e.Graphics,
            e.Node.Text,
            _tree.Font,
            e.Bounds,
            TreeText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x02000000;
            return cp;
        }
    }

    private void ConnectSelected()
    {
        if (_tree.SelectedNode != null)
            ConnectNode(_tree.SelectedNode);
        else
            SetStatus("Select a country, city, or server first.");
    }

    private async void ConnectNode(TreeNode node)
    {
        if (node.Tag is not RegionRow row || _connecting)
            return;

        _connecting = true;
        _connect.Enabled = false;
        try
        {
            await EnsureConnectedAsync();
            string message = await _client.ConnectRegionAsync(row, CancellationToken.None);
            SetStatus(message);
        }
        catch (Exception ex)
        {
            SetStatus(ex.GetBaseException().Message);
        }
        finally
        {
            _connect.Enabled = true;
            _connecting = false;
        }
    }

    private async Task LoadListAsync(bool force)
    {
        if (_loading)
            return;

        _loading = true;
        try
        {
            await EnsureConnectedAsync();
            ListResponse snapshot = await _client.ListAsync(CancellationToken.None);
            string fingerprint = Fingerprint(snapshot);
            if (!force && fingerprint == _fingerprint && _tree.Nodes.Count > 0)
            {
                SetReadyStatus(snapshot);
                return;
            }

            RebuildTree(snapshot);
            _fingerprint = fingerprint;
            SetReadyStatus(snapshot);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Waiting for ProtonVPN hook… run the loader.");
        }
        catch (Exception ex)
        {
            _client.Dispose();
            SetStatus(ex.GetBaseException().Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildTree(ListResponse snapshot)
    {
        string? selectedKey = KeyOf(_tree.SelectedNode);
        HashSet<string> expanded = [];
        CollectExpanded(_tree.Nodes, expanded);

        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        foreach (FreeRegion region in snapshot.Regions)
        {
            TreeNode country = new($"{region.Name}    {region.ServerCount}")
            {
                Tag = new RegionRow
                {
                    Code = region.Code,
                    Title = region.Name,
                    ServerCount = region.ServerCount
                }
            };

            foreach (CityCount city in region.Cities)
            {
                TreeNode cityNode = new($"{city.Name}    {city.ServerCount}")
                {
                    Tag = new RegionRow
                    {
                        Code = region.Code,
                        City = city.Name,
                        Title = city.Name,
                        ServerCount = city.ServerCount
                    }
                };

                foreach (ServerItem server in city.Servers)
                {
                    string label = server.Load > 0
                        ? $"{server.Name}    {server.Load}%"
                        : server.Name;
                    cityNode.Nodes.Add(new TreeNode(label)
                    {
                        Tag = new RegionRow
                        {
                            Code = region.Code,
                            City = city.Name,
                            ServerId = server.Id,
                            ServerName = server.Name,
                            Title = server.Name,
                            ServerCount = 1
                        }
                    });
                }

                country.Nodes.Add(cityNode);
                if (expanded.Contains(KeyOf(cityNode)!))
                    cityNode.Expand();
            }

            _tree.Nodes.Add(country);
            if (expanded.Contains(KeyOf(country)!))
                country.Expand();
        }

        if (selectedKey != null)
        {
            TreeNode? match = FindNode(_tree.Nodes, selectedKey);
            if (match != null)
                _tree.SelectedNode = match;
        }

        _tree.EndUpdate();
    }

    private static void CollectExpanded(TreeNodeCollection nodes, HashSet<string> expanded)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.IsExpanded)
            {
                string? key = KeyOf(node);
                if (key != null)
                    expanded.Add(key);
            }
            CollectExpanded(node.Nodes, expanded);
        }
    }

    private static TreeNode? FindNode(TreeNodeCollection nodes, string key)
    {
        foreach (TreeNode node in nodes)
        {
            if (KeyOf(node) == key)
                return node;
            TreeNode? child = FindNode(node.Nodes, key);
            if (child != null)
                return child;
        }

        return null;
    }

    private static string? KeyOf(TreeNode? node)
    {
        if (node?.Tag is not RegionRow row)
            return null;
        if (!string.IsNullOrWhiteSpace(row.ServerId))
            return $"{row.Code}/{row.City}/{row.ServerId}";
        if (!string.IsNullOrWhiteSpace(row.City))
            return $"{row.Code}/{row.City}";
        return row.Code;
    }

    private static string Fingerprint(ListResponse snapshot)
    {
        return string.Join("|", snapshot.Regions.Select(r =>
            $"{r.Code}:{r.ServerCount}:{string.Join(",", r.Cities.Select(c =>
                c.Name + c.ServerCount + ":" + string.Join(".", c.Servers.Select(s => s.Id))))}"));
    }

    private void SetReadyStatus(ListResponse snapshot)
    {
        int countries = snapshot.Regions.Count;
        int servers = snapshot.Regions.Sum(r => r.ServerCount);
        if (countries == 0)
        {
            SetStatus(snapshot.Ready
                ? "No free servers in the current list."
                : "Hook loaded, waiting for server list…");
            return;
        }

        SetStatus($"{countries} countries · {servers} free servers");
    }

    private async Task EnsureConnectedAsync()
    {
        if (_client.Connected)
            return;
        await _client.ConnectAsync(CancellationToken.None);
    }

    private void SetStatus(string text)
    {
        if (_status.Text != text)
            _status.Text = text;
    }
}
