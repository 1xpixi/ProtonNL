using System.Reflection;

namespace ProtonNL.Hook;

internal static class PickerOverlay
{
    private const string Tag = "ProtonNL.Picker";

    private static volatile bool Attached;

    public static void Start()
    {
        Thread thread = new(WaitAndAttach)
        {
            IsBackground = true,
            Name = "ProtonNL-picker"
        };
        thread.Start();
    }

    private static void WaitAndAttach()
    {
        for (int i = 0; i < 120 && !Attached; i++)
        {
            try
            {
                object? window = FindMainWindow();
                if (window != null)
                {
                    bool ok = false;
                    using ManualResetEventSlim done = new(false);
                    if (Xaml.Enqueue(window, () =>
                        {
                            ok = TryAttach();
                            done.Set();
                        }))
                    {
                        if (!done.Wait(TimeSpan.FromSeconds(2)))
                            Logger.Write("picker attach enqueue timed out");
                        else if (ok)
                        {
                            Attached = true;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("picker attach attempt failed: " + ex.Message);
            }

            Thread.Sleep(500);
        }

        if (!Attached)
            Logger.Write("timed out waiting for ProtonVPN MainWindow");
    }

    private static object? FindMainWindow()
    {
        Type? appType = Xaml.TryType("Microsoft.UI.Xaml.Application");
        object? current = appType?.GetProperty("Current")?.GetValue(null);
        if (current == null)
            return null;

        object? window = current.GetType().GetProperty("MainWindow")?.GetValue(current);
        if (window != null)
            return window;

        Type? activatorType = Xaml.TryType("ProtonVPN.Client.Services.Activation.MainWindowActivator");
        MethodInfo? getService = current.GetType().GetMethod("GetService", BindingFlags.Public | BindingFlags.Static, [typeof(Type)]);
        if (activatorType != null && getService != null)
        {
            object? activator = getService.Invoke(null, [activatorType]);
            window = activator?.GetType().GetProperty("Host")?.GetValue(activator);
        }

        return window;
    }

    private static void Attach()
    {
        TryAttach();
    }

    private static bool TryAttach()
    {
        try
        {
            object? window = FindMainWindow();
            if (window == null)
            {
                Logger.Write("picker: MainWindow not found");
                return false;
            }

            object? panel = FindHostPanel(window);
            if (panel == null)
            {
                object? content = Xaml.Get(window, "Content");
                Logger.Write("picker: host panel not ready window=" + window.GetType().FullName
                    + " content=" + (content?.GetType().FullName ?? "null"));
                return false;
            }

            if (FindTagged(panel) != null)
            {
                Logger.Write("picker already attached");
                return true;
            }

            Host host = new();
            Xaml.Add(panel, host.Build());
            host.Reload();
            Logger.Write("picker attached to " + panel.GetType().Name);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Write("picker attach failed: " + ex);
            return false;
        }
    }

    private static object? FindHostPanel(object window)
    {
        object? container = ReadMember(window, "WindowContainer")
            ?? Xaml.Get(window, "Content");
        if (container == null)
            return null;

        if (HasChildren(container))
            return container;

        object? inner = Xaml.Get(container, "Content") ?? ReadMember(container, "Content");
        if (HasChildren(inner))
            return inner;

        return null;
    }

    private static object? ReadMember(object instance, string name)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo? prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
                return prop.GetValue(instance);

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(instance);
        }

        return null;
    }

    private static bool HasChildren(object? element)
    {
        if (element == null)
            return false;
        object? children = Xaml.Get(element, "Children");
        return children != null && children.GetType().GetMethod("Add") != null;
    }

    private static object? FindTagged(object parent)
    {
        object? children = Xaml.Get(parent, "Children");
        if (children is not System.Collections.IEnumerable items)
            return null;

        foreach (object child in items)
        {
            if (Equals(Xaml.Get(child, "Tag"), Tag))
                return child;
        }

        return null;
    }

    private sealed class Host
    {
        private object? _list;
        private object? _hint;
        private object? _summary;
        private object? _search;
        private object? _connect;
        private object? _body;
        private object? _root;
        private object? _launcher;
        private object? _animation;
        private object? _parent;
        private bool _open;
        private bool _busy;
        private Pick? _selected;
        private List<FreeRegion> _regions = [];
        private readonly HashSet<string> _openKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(object button, Pick pick)> _rows = [];

        public object Build()
        {
            _root = Border(Brush(255, 24, 23, 32), 0);
            Xaml.Set(_root, "Tag", Tag);
            Xaml.Set(_root, "Width", 156.0);
            Xaml.Set(_root, "Height", 48.0);
            Xaml.Set(_root, "Margin", Xaml.Thickness(16, 56, 16, 18));
            Xaml.Set(_root, "CornerRadius", Xaml.Corner(16));
            Xaml.Set(_root, "RequestedTheme", Xaml.Enum("Microsoft.UI.Xaml.ElementTheme", "Dark"));
            Xaml.Set(_root, "HorizontalAlignment", Xaml.Enum("Microsoft.UI.Xaml.HorizontalAlignment", "Right"));
            Xaml.Set(_root, "VerticalAlignment", Xaml.Enum("Microsoft.UI.Xaml.VerticalAlignment", "Bottom"));

            object grid = Xaml.New("Microsoft.UI.Xaml.Controls.Grid");
            _launcher = Button("", false);
            Xaml.Set(_launcher, "Background", Brush(0, 0, 0, 0));
            Xaml.Set(_launcher, "BorderThickness", Xaml.Thickness(0));
            Xaml.Set(_launcher, "CornerRadius", Xaml.Corner(16));
            Xaml.Set(_launcher, "Padding", Xaml.Thickness(12, 0, 12, 0));
            object launchContent = Xaml.New("Microsoft.UI.Xaml.Controls.StackPanel");
            Xaml.Set(launchContent, "Orientation", Xaml.Enum("Microsoft.UI.Xaml.Controls.Orientation", "Horizontal"));
            Xaml.Set(launchContent, "Spacing", 9.0);
            object mark = Border(Brush(255, 114, 83, 237), 0);
            Xaml.Set(mark, "Width", 26.0);
            Xaml.Set(mark, "Height", 26.0);
            Xaml.Set(mark, "CornerRadius", Xaml.Corner(8));
            object symbol = Icon("\uE774", 14);
            Xaml.Set(mark, "Child", symbol);
            Xaml.Add(launchContent, mark);
            Xaml.Add(launchContent, Text("Free servers", 12, 241, 239, 250));
            Xaml.Set(_launcher, "Content", launchContent);
            Accessible(_launcher, "Open free servers");
            Xaml.On(_launcher, "Click", _ => Toggle());
            Xaml.Add(grid, _launcher);

            _body = Xaml.New("Microsoft.UI.Xaml.Controls.Grid");
            Xaml.Set(_body, "Visibility", Xaml.Enum("Microsoft.UI.Xaml.Visibility", "Collapsed"));
            Xaml.Set(_body, "KeyboardAcceleratorPlacementMode", Xaml.Enum("Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode", "Hidden"));
            Xaml.Row(_body, Xaml.GridLengthAuto());
            Xaml.Row(_body, Xaml.GridLengthAuto());
            Xaml.Row(_body, Xaml.GridLengthStar());
            Xaml.Row(_body, Xaml.GridLengthAuto());
            Xaml.Add(_body, BuildHeader());
            object escape = Xaml.New("Microsoft.UI.Xaml.Input.KeyboardAccelerator");
            Xaml.Set(escape, "Key", Xaml.Enum("Windows.System.VirtualKey", "Escape"));
            Xaml.On(escape, "Invoked", _ => { if (_open) Toggle(); });
            object shortcuts = Xaml.Get(_body, "KeyboardAccelerators")!;
            shortcuts.GetType().GetMethod("Add")!.Invoke(shortcuts, [escape]);

            _search = Xaml.New("Microsoft.UI.Xaml.Controls.TextBox");
            Xaml.Set(_search, "PlaceholderText", "Search country, city or server");
            Xaml.Set(_search, "FontSize", 12.0);
            Xaml.Set(_search, "Height", 34.0);
            Xaml.Set(_search, "MinHeight", 0.0);
            Xaml.Set(_search, "Padding", Xaml.Thickness(10, 6, 10, 6));
            Xaml.Set(_search, "CornerRadius", Xaml.Corner(8));
            Xaml.Set(_search, "Background", Brush(255, 32, 31, 42));
            Xaml.Set(_search, "BorderBrush", Brush(255, 57, 54, 72));
            Xaml.Set(_search, "Margin", Xaml.Thickness(14, 0, 14, 10));
            Accessible(_search, "Search free servers");
            Xaml.On(_search, "TextChanged", _ => Reload(filterOnly: true));
            Xaml.SetRow(_search, 1);
            Xaml.Add(_body, _search);

            object scroll = Xaml.New("Microsoft.UI.Xaml.Controls.ScrollViewer");
            Xaml.Set(scroll, "VerticalScrollBarVisibility", Xaml.Enum("Microsoft.UI.Xaml.Controls.ScrollBarVisibility", "Auto"));
            Xaml.Set(scroll, "HorizontalScrollBarVisibility", Xaml.Enum("Microsoft.UI.Xaml.Controls.ScrollBarVisibility", "Disabled"));
            Xaml.Set(scroll, "Padding", Xaml.Thickness(10, 0, 10, 4));
            _list = Xaml.New("Microsoft.UI.Xaml.Controls.StackPanel");
            Xaml.Set(scroll, "Content", _list);
            Xaml.SetRow(scroll, 2);
            Xaml.Add(_body, scroll);

            object footer = Xaml.New("Microsoft.UI.Xaml.Controls.StackPanel");
            Xaml.Set(footer, "Padding", Xaml.Thickness(14, 10, 14, 14));
            Xaml.Set(footer, "Spacing", 8.0);
            _hint = Text("Finding free servers…", 11, 162, 157, 181);
            Xaml.Set(_hint, "TextTrimming", Xaml.Enum("Microsoft.UI.Xaml.TextTrimming", "CharacterEllipsis"));
            Xaml.Add(footer, _hint);
            _connect = Button("Quick connect", true);
            Xaml.Set(_connect, "Height", 36.0);
            Xaml.Set(_connect, "IsEnabled", false);
            Xaml.On(_connect, "Click", _ => ConnectSelected());
            Xaml.Add(footer, _connect);
            Xaml.SetRow(footer, 3);
            Xaml.Add(_body, footer);
            Xaml.Add(grid, _body);
            Xaml.Set(_root, "Child", grid);
            Xaml.On(_root, "Loaded", _ =>
            {
                _parent = Xaml.Get(_root, "Parent");
                if (_parent != null)
                    Xaml.On(_parent, "SizeChanged", _ => Resize());
            });
            return _root;
        }

        private object BuildHeader()
        {
            object bar = Xaml.New("Microsoft.UI.Xaml.Controls.Grid");
            Xaml.Set(bar, "Padding", Xaml.Thickness(16, 15, 12, 12));
            object left = Xaml.New("Microsoft.UI.Xaml.Controls.StackPanel");
            Xaml.Set(left, "Spacing", 3.0);
            Xaml.Add(left, Text("Free servers", 16, 244, 241, 252));
            _summary = Text("PROTON NL", 10, 162, 153, 191);
            Xaml.Add(left, _summary);
            Xaml.Add(bar, left);

            object hide = Button("", false);
            Xaml.Set(hide, "Content", Icon("\uE74B", 12));
            Xaml.Set(hide, "HorizontalAlignment", Xaml.Enum("Microsoft.UI.Xaml.HorizontalAlignment", "Right"));
            Xaml.Set(hide, "VerticalAlignment", Xaml.Enum("Microsoft.UI.Xaml.VerticalAlignment", "Top"));
            Xaml.Set(hide, "Width", 28.0);
            Xaml.Set(hide, "Height", 28.0);
            Xaml.Set(hide, "Padding", Xaml.Thickness(0));
            Accessible(hide, "Collapse free servers");
            Xaml.On(hide, "Click", _ => Toggle());
            Xaml.Add(bar, hide);
            return bar;
        }

        private double CardWidth => Math.Max(156, Math.Min(336, Convert.ToDouble(_parent == null ? 368.0 : Xaml.Get(_parent, "ActualWidth")) - 32));
        private double CardHeight => Math.Max(180, Math.Min(580, Convert.ToDouble(_parent == null ? 654.0 : Xaml.Get(_parent, "ActualHeight")) - 74));

        private void Resize()
        {
            if (!_open || _root == null)
                return;
            StopAnimation();
            Xaml.Set(_root, "Width", CardWidth);
            Xaml.Set(_root, "Height", CardHeight);
            Xaml.Set(_body!, "Opacity", 1.0);
        }

        private void StopAnimation()
        {
            if (_animation == null)
                return;
            object animation = _animation;
            _animation = null;
            animation.GetType().GetMethod("Stop")!.Invoke(animation, null);
        }

        private void Toggle()
        {
            if (_root == null || _body == null || _launcher == null)
                return;
            double width = Convert.ToDouble(Xaml.Get(_root, "Width"));
            double height = Convert.ToDouble(Xaml.Get(_root, "Height"));
            double opacity = Convert.ToDouble(Xaml.Get(_body, "Opacity"));
            StopAnimation();
            _open = !_open;
            Xaml.Set(_root, "Width", _open ? CardWidth : 156.0);
            Xaml.Set(_root, "Height", _open ? CardHeight : 48.0);
            Xaml.Set(_body, "Opacity", _open ? 1.0 : 0.0);
            Xaml.Set(_body, "IsHitTestVisible", _open);
            Xaml.Set(_body, "Visibility", Xaml.Enum("Microsoft.UI.Xaml.Visibility", "Visible"));
            Xaml.Set(_launcher, "Visibility", Xaml.Enum("Microsoft.UI.Xaml.Visibility", "Collapsed"));

            object storyboard = Xaml.New("Microsoft.UI.Xaml.Media.Animation.Storyboard");
            _animation = storyboard;
            Animate(storyboard, _root, "Width", width, _open ? CardWidth : 156.0);
            Animate(storyboard, _root, "Height", height, _open ? CardHeight : 48.0);
            Animate(storyboard, _body, "Opacity", _open ? 0.0 : opacity, _open ? 1.0 : 0.0);
            Xaml.On(storyboard, "Completed", _ =>
            {
                if (!ReferenceEquals(_animation, storyboard))
                    return;
                StopAnimation();
                Xaml.Set(_body, "Visibility", Xaml.Enum("Microsoft.UI.Xaml.Visibility", _open ? "Visible" : "Collapsed"));
                Xaml.Set(_launcher, "Visibility", Xaml.Enum("Microsoft.UI.Xaml.Visibility", _open ? "Collapsed" : "Visible"));
                if (!_open)
                    _launcher.GetType().GetMethod("Focus")!.Invoke(_launcher, [Xaml.Enum("Microsoft.UI.Xaml.FocusState", "Programmatic")]);
            });
            storyboard.GetType().GetMethod("Begin")!.Invoke(storyboard, null);
        }

        private static void Animate(object storyboard, object target, string property, double from, double to)
        {
            object animation = Xaml.New("Microsoft.UI.Xaml.Media.Animation.DoubleAnimation");
            Xaml.Set(animation, "From", from);
            Xaml.Set(animation, "To", to);
            Xaml.Set(animation, "Duration", Activator.CreateInstance(Xaml.Type("Microsoft.UI.Xaml.Duration"), TimeSpan.FromMilliseconds(260)));
            Xaml.Set(animation, "EnableDependentAnimation", true);
            object ease = Xaml.New("Microsoft.UI.Xaml.Media.Animation.CubicEase");
            Xaml.Set(ease, "EasingMode", Xaml.Enum("Microsoft.UI.Xaml.Media.Animation.EasingMode", "EaseOut"));
            Xaml.Set(animation, "EasingFunction", ease);
            Type type = Xaml.Type("Microsoft.UI.Xaml.Media.Animation.Storyboard");
            type.GetMethod("SetTarget")!.Invoke(null, [animation, target]);
            type.GetMethod("SetTargetProperty")!.Invoke(null, [animation, property]);
            Xaml.Add(storyboard, animation);
        }

        public void Reload(bool filterOnly = false)
        {
            if (!filterOnly)
                _regions = ConnectionBridge.GetFreeRegions();

            if (_list == null)
                return;

            Xaml.Clear(_list);
            _rows.Clear();
            string q = (_search == null ? "" : Xaml.Get(_search, "Text") as string ?? "").Trim();
            int shown = 0;

            foreach (FreeRegion region in _regions)
            {
                List<(CityCount city, List<ServerItem> servers)> cities = [];
                foreach (CityCount city in region.Cities)
                {
                    List<ServerItem> servers = city.Servers
                        .Where(s => Match(q, region.Name, region.Code, city.Name, s.Name))
                        .ToList();
                    if (q.Length > 0 && servers.Count == 0 && !Match(q, region.Name, region.Code, city.Name))
                        continue;
                    cities.Add((city, q.Length > 0 ? servers : city.Servers));
                }

                if (q.Length > 0 && cities.Count == 0 && !Match(q, region.Name, region.Code))
                    continue;

                bool countryOpen = q.Length > 0 || _openKeys.Contains(region.Code);
                string countryKey = region.Code;
                Pick countryPick = new(region.Code, null, null, null, region.Name);
                object countryBtn = TreeButton(region.Name, q.Length > 0 ? cities.Sum(c => c.servers.Count) : region.ServerCount, 0, countryPick, countryOpen, region.Code);
                Xaml.On(countryBtn, "Click", _ =>
                {
                    ToggleKey(countryKey);
                    Select(countryPick);
                });
                Xaml.Add(_list, countryBtn);

                if (!countryOpen)
                    continue;

                foreach ((CityCount city, List<ServerItem> servers) in cities)
                {
                    string cityKey = region.Code + "/" + city.Name;
                    bool cityOpen = q.Length > 0 || _openKeys.Contains(cityKey);
                    Pick cityPick = new(region.Code, city.Name, null, null, $"{region.Name} / {city.Name}");
                    object cityBtn = TreeButton(city.Name, servers.Count, 1, cityPick, cityOpen);
                    Xaml.On(cityBtn, "Click", _ =>
                    {
                        ToggleKey(cityKey);
                        Select(cityPick);
                    });
                    Xaml.Add(_list, cityBtn);

                    if (!cityOpen)
                        continue;

                    foreach (ServerItem server in servers)
                    {
                        Pick pick = new(region.Code, city.Name, server.Id, server.Name, $"{region.Name} / {city.Name} / {server.Name}");
                        object row = TreeButton(server.Name, 0, 2, pick, load: server.Load);
                        Xaml.On(row, "Click", _ => Select(pick));
                        Xaml.On(row, "DoubleTapped", _ =>
                        {
                            Select(pick);
                            ConnectSelected();
                        });
                        Xaml.Add(_list, row);
                        shown++;
                    }
                }
            }

            if (_summary != null)
                Xaml.Set(_summary, "Text", $"{_regions.Count} countries  ·  {_regions.Sum(r => r.ServerCount)} servers");
            if (_regions.Count == 0 || (shown == 0 && q.Length > 0))
            {
                object empty = Xaml.New("Microsoft.UI.Xaml.Controls.StackPanel");
                Xaml.Set(empty, "Margin", Xaml.Thickness(10, 24, 10, 24));
                Xaml.Set(empty, "Spacing", 6.0);
                Xaml.Add(empty, Text(_regions.Count == 0 ? "Finding free servers…" : "No servers found", 13, 229, 225, 241));
                Xaml.Add(empty, Text(_regions.Count == 0 ? "Waiting for ProtonVPN to finish loading." : "Try another country, city or server name.", 11, 162, 157, 181));
                Xaml.Add(_list, empty);
            }
            if (!_busy)
                Hint(_selected?.Label ?? "Auto-select the lowest-load server");

            EnableConnect();
            if (_regions.Count == 0)
                ScheduleReload();
        }

        private void ToggleKey(string key)
        {
            if (!_openKeys.Add(key))
                _openKeys.Remove(key);
            Reload(filterOnly: true);
        }

        private void ScheduleReload()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(1500);
                object? window = FindMainWindow();
                if (window != null)
                    Xaml.Enqueue(window, () => Reload());
            });
        }

        private void Select(Pick pick)
        {
            _selected = pick;
            Hint(pick.Label);
            foreach ((object button, Pick rowPick) in _rows)
                StyleSelection(button, rowPick == pick);
            EnableConnect();
        }

        private void EnableConnect()
        {
            if (_connect != null)
            {
                Xaml.Set(_connect, "IsEnabled", !_busy && Lowest() != null);
                Xaml.Set(_connect, "Content", _busy ? "Connecting…" : _selected == null ? "Quick connect" : "Connect");
            }
        }

        private Pick? Lowest()
        {
            Pick? best = null;
            int bestLoad = int.MaxValue;
            foreach (FreeRegion region in _regions)
            {
                if (_selected?.Country != null && region.Code != _selected.Country)
                    continue;
                foreach (CityCount city in region.Cities)
                {
                    if (_selected?.City != null && !string.Equals(city.Name, _selected.City, StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (ServerItem server in city.Servers)
                    {
                        if (_selected?.ServerId != null && server.Id != _selected.ServerId)
                            continue;
                        int load = server.Load > 0 ? server.Load : 999;
                        if (load >= bestLoad)
                            continue;
                        bestLoad = load;
                        best = new Pick(region.Code, city.Name, server.Id, server.Name, server.Name);
                    }
                }
            }

            return best;
        }

        private void ConnectSelected()
        {
            Pick? target = _selected?.ServerId != null ? _selected : Lowest();
            if (target == null || target.Country == null)
            {
                Hint("No free server selected.");
                return;
            }

            _busy = true;
            EnableConnect();
            Hint("Connecting to " + (target.ServerName ?? target.Label) + "…");
            string country = target.Country;
            string? city = target.City;
            string? id = target.ServerId;
            string? name = target.ServerName;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string message = ConnectionBridge.Connect(country, city, id, name);
                object? window = FindMainWindow();
                if (window != null)
                {
                    Xaml.Enqueue(window, () =>
                    {
                        _busy = false;
                        Hint(message);
                        EnableConnect();
                    });
                }
            });
        }

        private void Hint(string text)
        {
            if (_hint != null)
                Xaml.Set(_hint, "Text", text);
        }

        private static bool Match(string q, params string?[] parts)
        {
            if (q.Length == 0)
                return true;
            return parts.Any(p => !string.IsNullOrWhiteSpace(p) && p.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        private static object Text(string value, double size, byte r, byte g, byte b)
        {
            object block = Xaml.New("Microsoft.UI.Xaml.Controls.TextBlock");
            Xaml.Set(block, "Text", value);
            Xaml.Set(block, "FontSize", size);
            Xaml.Set(block, "Foreground", Brush(255, r, g, b));
            Xaml.Set(block, "VerticalAlignment", Xaml.Enum("Microsoft.UI.Xaml.VerticalAlignment", "Center"));
            return block;
        }

        private static object Button(string label, bool accent)
        {
            object button = Xaml.New("Microsoft.UI.Xaml.Controls.Button");
            Xaml.Set(button, "Content", label);
            Xaml.Set(button, "HorizontalAlignment", Xaml.Enum("Microsoft.UI.Xaml.HorizontalAlignment", "Stretch"));
            Xaml.Set(button, "MinHeight", 0.0);
            Xaml.Set(button, "MinWidth", 0.0);
            Xaml.Set(button, "FontSize", 12.0);
            Xaml.Set(button, "Padding", Xaml.Thickness(12, 8, 12, 8));
            if (accent)
            {
                Xaml.Set(button, "Background", Brush(255, 109, 74, 255));
                Xaml.Set(button, "Foreground", Brush(255, 255, 255, 255));
            }
            else
            {
                Xaml.Set(button, "Background", Brush(255, 42, 41, 52));
                Xaml.Set(button, "Foreground", Brush(255, 220, 220, 226));
            }
            StateBrush(button, "ButtonBackgroundPointerOver", accent ? Brush(255, 129, 98, 255) : Brush(255, 40, 36, 53));
            StateBrush(button, "ButtonBackgroundPressed", accent ? Brush(255, 91, 64, 217) : Brush(255, 48, 43, 64));
            StateBrush(button, "ButtonBorderBrushPointerOver", Brush(0, 0, 0, 0));
            StateBrush(button, "ButtonBorderBrushPressed", Brush(0, 0, 0, 0));
            StateBrush(button, "ButtonForegroundPointerOver", Brush(255, 255, 255, 255));
            StateBrush(button, "ButtonForegroundPressed", Brush(255, 255, 255, 255));

            Xaml.Set(button, "CornerRadius", Xaml.Corner(8));
            return button;
        }

        private object TreeButton(string label, int count, int indent, Pick pick, bool expanded = false, string? code = null, int load = 0)
        {
            object button = Button("", false);
            Xaml.Set(button, "HorizontalContentAlignment", Xaml.Enum("Microsoft.UI.Xaml.HorizontalAlignment", "Stretch"));
            Xaml.Set(button, "Height", indent == 0 ? 35.0 : 30.0);
            Xaml.Set(button, "Padding", Xaml.Thickness(8, 0, 8, 0));
            Xaml.Set(button, "Margin", Xaml.Thickness(indent * 12, 1, 0, 1));
            object grid = Xaml.New("Microsoft.UI.Xaml.Controls.Grid");
            Column(grid, Xaml.GridLengthAuto());
            Column(grid, Xaml.GridLengthStar());
            Column(grid, Xaml.GridLengthAuto());

            object leading = Xaml.New("Microsoft.UI.Xaml.Controls.StackPanel");
            Xaml.Set(leading, "Orientation", Xaml.Enum("Microsoft.UI.Xaml.Controls.Orientation", "Horizontal"));
            Xaml.Set(leading, "Spacing", 7.0);
            if (indent < 2)
            {
                object arrow = Icon(expanded ? "\uE70D" : "\uE76C", 8);
                Xaml.Set(arrow, "Width", 10.0);
                Xaml.Add(leading, arrow);
            }
            if (code != null)
            {
                object badge = Border(Brush(255, 46, 41, 63), 0);
                Xaml.Set(badge, "BorderThickness", Xaml.Thickness(0));
                Xaml.Set(badge, "Width", 27.0);
                Xaml.Set(badge, "Height", 21.0);
                Xaml.Set(badge, "CornerRadius", Xaml.Corner(5));
                object countryCode = Text(code.ToUpperInvariant(), 9, 192, 180, 235);
                Xaml.Set(countryCode, "HorizontalAlignment", Xaml.Enum("Microsoft.UI.Xaml.HorizontalAlignment", "Center"));
                Xaml.Set(badge, "Child", countryCode);
                Xaml.Add(leading, badge);
            }
            Xaml.Set(leading, "Margin", Xaml.Thickness(0, 0, indent == 2 ? 0 : 9, 0));
            Xaml.Add(grid, leading);
            object name = Text(label, indent == 0 ? 13 : 12, 231, 227, 241);
            Xaml.Set(name, "TextTrimming", Xaml.Enum("Microsoft.UI.Xaml.TextTrimming", "CharacterEllipsis"));
            AtColumn(name, 1);
            Xaml.Add(grid, name);

            object meta = Xaml.New("Microsoft.UI.Xaml.Controls.StackPanel");
            Xaml.Set(meta, "Orientation", Xaml.Enum("Microsoft.UI.Xaml.Controls.Orientation", "Horizontal"));
            Xaml.Set(meta, "Spacing", 6.0);
            Xaml.Set(meta, "Margin", Xaml.Thickness(10, 0, 0, 0));
            Xaml.Set(meta, "VerticalAlignment", Xaml.Enum("Microsoft.UI.Xaml.VerticalAlignment", "Center"));
            if (indent == 2 && load > 0)
            {
                object dot = Border(load < 60 ? Brush(255, 91, 200, 163) : load < 85 ? Brush(255, 223, 181, 102) : Brush(255, 227, 129, 143), 0);
                Xaml.Set(dot, "Width", 5.0);
                Xaml.Set(dot, "Height", 5.0);
                Xaml.Set(dot, "CornerRadius", Xaml.Corner(3));
                Xaml.Set(dot, "BorderThickness", Xaml.Thickness(0));
                Xaml.Add(meta, dot);
            }
            Xaml.Add(meta, Text(indent == 2 ? load > 0 ? $"{load}%" : "—" : count.ToString(), 11, 159, 153, 176));
            AtColumn(meta, 2);
            Xaml.Add(grid, meta);
            Xaml.Set(button, "Content", grid);
            Accessible(button, indent == 2 ? $"{label}, {(load > 0 ? load + "% load" : "load unavailable")}" : $"{label}, {count} servers, {(expanded ? "expanded" : "collapsed")}");
            _rows.Add((button, pick));
            StyleSelection(button, pick == _selected);
            return button;
        }

        private static void StyleSelection(object button, bool selected)
        {
            Xaml.Set(button, "Background", selected ? Brush(255, 49, 39, 78) : Brush(0, 0, 0, 0));
            Xaml.Set(button, "BorderBrush", selected ? Brush(255, 112, 84, 186) : Brush(0, 0, 0, 0));
            Xaml.Set(button, "BorderThickness", Xaml.Thickness(1));
            StateBrush(button, "ButtonBackgroundPointerOver", selected ? Brush(255, 62, 48, 97) : Brush(255, 40, 36, 53));
            StateBrush(button, "ButtonBackgroundPressed", selected ? Brush(255, 54, 41, 87) : Brush(255, 48, 43, 64));
            StateBrush(button, "ButtonBorderBrushPointerOver", selected ? Brush(255, 127, 97, 203) : Brush(0, 0, 0, 0));
            StateBrush(button, "ButtonBorderBrushPressed", selected ? Brush(255, 127, 97, 203) : Brush(0, 0, 0, 0));
        }

        private static void StateBrush(object button, string key, object brush)
            => ((IDictionary<object, object>)Xaml.Get(button, "Resources")!)[key] = brush;

        private static void Column(object grid, object length)
        {
            object definition = Xaml.New("Microsoft.UI.Xaml.Controls.ColumnDefinition");
            Xaml.Set(definition, "Width", length);
            object columns = Xaml.Get(grid, "ColumnDefinitions")!;
            columns.GetType().GetMethod("Add")!.Invoke(columns, [definition]);
        }

        private static void AtColumn(object element, int column)
            => Xaml.Type("Microsoft.UI.Xaml.Controls.Grid").GetMethod("SetColumn")!.Invoke(null, [element, column]);

        private static object Icon(string glyph, double size)
        {
            object icon = Xaml.New("Microsoft.UI.Xaml.Controls.FontIcon");
            Xaml.Set(icon, "Glyph", glyph);
            Xaml.Set(icon, "FontSize", size);
            Xaml.Set(icon, "FontFamily", Activator.CreateInstance(Xaml.Type("Microsoft.UI.Xaml.Media.FontFamily"), "Segoe Fluent Icons"));
            return icon;
        }

        private static void Accessible(object element, string name)
            => Xaml.Type("Microsoft.UI.Xaml.Automation.AutomationProperties").GetMethod("SetName")!.Invoke(null, [element, name]);

        private static object Border(object brush, double pad)
        {
            object border = Xaml.New("Microsoft.UI.Xaml.Controls.Border");
            Xaml.Set(border, "Background", brush);
            if (pad > 0)
                Xaml.Set(border, "Padding", Xaml.Thickness(pad));
            Xaml.Set(border, "BorderBrush", Brush(255, 62, 56, 78));
            Xaml.Set(border, "BorderThickness", Xaml.Thickness(1));
            return border;
        }

        private static object Brush(byte a, byte r, byte g, byte b) => Xaml.Brush(a, r, g, b);

        private sealed record Pick(string? Country, string? City, string? ServerId, string? ServerName, string Label);
    }
}
