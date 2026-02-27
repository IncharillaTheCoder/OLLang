using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using Ollang.Values;
using Ollang.Interpreter;

namespace Ollang.Gui
{
    public class GuiValue : IValue
    {
        public Control Control { get; }
        public GuiValue(Control control) => Control = control;
        public double AsNumber() => Control.GetHashCode();
        public bool AsBool() => Control != null;
        public override string ToString() => $"<{Control.GetType().Name}>";
        public IValue GetIndex(IValue index) => new NullValue();
        public void SetIndex(IValue index, IValue value) { }
    }

    public static class GuiRuntime
    {
        private static Thread? _uiThread;
        private static Form? _mainForm;
        private static bool _appRunning;
        private static readonly Dictionary<string, Control> _controls = new();
        private static readonly Dictionary<string, ICallable> _handlers = new();
        private static InterpreterState? _state;

        static Color ParseColor(string name)
        {
            return name.StartsWith("#")
                ? ColorTranslator.FromHtml(name)
                : Color.FromName(name);
        }

        static Font ParseFont(string family, float size, bool bold, bool italic)
        {
            var style = FontStyle.Regular;
            if (bold) style |= FontStyle.Bold;
            if (italic) style |= FontStyle.Italic;
            return new Font(family, size, style);
        }

        static void InvokeHandler(string id, string eventName, string extra = "")
        {
            var key = $"{id}.{eventName}";
            if (_handlers.TryGetValue(key, out var handler) && _state != null)
            {
                try
                {
                    var args = new List<IValue> { new StringValue(id) };
                    if (!string.IsNullOrEmpty(extra)) args.Add(new StringValue(extra));
                    handler.Call(_state, args);
                }
                catch { }
            }
        }

        static void ApplyBaseProps(Control ctrl, string id, List<IValue> args, int startIdx)
        {
            _controls[id] = ctrl;
            ctrl.Name = id;

            for (int i = startIdx; i < args.Count - 1; i += 2)
            {
                var prop = args[i].ToString().ToLower();
                var val = args[i + 1];
                switch (prop)
                {
                    case "x": ctrl.Left = (int)val.AsNumber(); break;
                    case "y": ctrl.Top = (int)val.AsNumber(); break;
                    case "width": case "w": ctrl.Width = (int)val.AsNumber(); break;
                    case "height": case "h": ctrl.Height = (int)val.AsNumber(); break;
                    case "bg": case "background": ctrl.BackColor = ParseColor(val.ToString()); break;
                    case "fg": case "foreground": case "color": ctrl.ForeColor = ParseColor(val.ToString()); break;
                    case "font": ctrl.Font = new Font(val.ToString(), ctrl.Font.Size); break;
                    case "fontsize": ctrl.Font = new Font(ctrl.Font.FontFamily, (float)val.AsNumber()); break;
                    case "bold": ctrl.Font = ParseFont(ctrl.Font.FontFamily.Name, ctrl.Font.Size, val.AsBool(), ctrl.Font.Italic); break;
                    case "visible": ctrl.Visible = val.AsBool(); break;
                    case "enabled": ctrl.Enabled = val.AsBool(); break;
                    case "tooltip":
                        var tt = new ToolTip();
                        tt.SetToolTip(ctrl, val.ToString());
                        break;
                    case "anchor":
                        ctrl.Anchor = val.ToString().ToLower() switch
                        {
                            "all" => AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                            "top" => AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                            "bottom" => AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                            _ => AnchorStyles.Top | AnchorStyles.Left
                        };
                        break;
                    case "cursor":
                        ctrl.Cursor = val.ToString().ToLower() switch
                        {
                            "hand" => Cursors.Hand,
                            "cross" => Cursors.Cross,
                            "wait" => Cursors.WaitCursor,
                            "text" => Cursors.IBeam,
                            _ => Cursors.Default
                        };
                        break;
                }
            }
        }

        public static void RegisterBuiltins(Dictionary<string, ICallable> builtins)
        {
            builtins["Gui.window"] = new BuiltinFunctionValue("Gui.window", (state, args) =>
            {
                var title = args.Count > 0 ? args[0].ToString() : "OLLang App";
                var w = args.Count > 1 ? (int)args[1].AsNumber() : 800;
                var h = args.Count > 2 ? (int)args[2].AsNumber() : 600;
                _state = state;

                _uiThread = new Thread(() =>
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    _mainForm = new Form
                    {
                        Text = title,
                        Width = w,
                        Height = h,
                        StartPosition = FormStartPosition.CenterScreen,
                        Font = new Font("Segoe UI", 10)
                    };
                    _controls["_main"] = _mainForm;
                    _appRunning = true;

                    _mainForm.FormClosed += (s, e) => { _appRunning = false; };

                    for (int i = 3; i < args.Count - 1; i += 2)
                    {
                        var prop = args[i].ToString().ToLower();
                        var val = args[i + 1];
                        switch (prop)
                        {
                            case "bg": case "background": _mainForm.BackColor = ParseColor(val.ToString()); break;
                            case "fg": case "foreground": _mainForm.ForeColor = ParseColor(val.ToString()); break;
                            case "resizable": _mainForm.FormBorderStyle = val.AsBool() ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle; break;
                            case "icon":
                                try { _mainForm.Icon = new Icon(val.ToString()); } catch { }
                                break;
                            case "topmost": _mainForm.TopMost = val.AsBool(); break;
                            case "opacity": _mainForm.Opacity = val.AsNumber(); break;
                        }
                    }

                    Application.Run(_mainForm);
                });
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.IsBackground = true;
                _uiThread.Start();

                while (!_appRunning) Thread.Sleep(10);
                return new GuiValue(_mainForm);
            });

            builtins["Gui.button"] = new BuiltinFunctionValue("Gui.button", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "btn";
                var text = args.Count > 1 ? args[1].ToString() : "Button";
                Button? btn = null;

                _mainForm?.Invoke(() =>
                {
                    btn = new Button
                    {
                        Text = text,
                        FlatStyle = FlatStyle.Flat,
                        Cursor = Cursors.Hand,
                        AutoSize = true,
                        Padding = new Padding(8, 4, 8, 4)
                    };
                    btn.Click += (s, e) => InvokeHandler(id, "click");
                    ApplyBaseProps(btn, id, args, 2);
                    _mainForm.Controls.Add(btn);
                });

                while (btn == null) Thread.Sleep(1);
                return new GuiValue(btn);
            });

            builtins["Gui.label"] = new BuiltinFunctionValue("Gui.label", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "lbl";
                var text = args.Count > 1 ? args[1].ToString() : "";
                Label? lbl = null;

                _mainForm?.Invoke(() =>
                {
                    lbl = new Label { Text = text, AutoSize = true };
                    ApplyBaseProps(lbl, id, args, 2);
                    _mainForm.Controls.Add(lbl);
                });

                while (lbl == null) Thread.Sleep(1);
                return new GuiValue(lbl);
            });

            builtins["Gui.input"] = new BuiltinFunctionValue("Gui.input", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "input";
                TextBox? tb = null;

                _mainForm?.Invoke(() =>
                {
                    tb = new TextBox { Width = 200, BorderStyle = BorderStyle.FixedSingle };
                    if (args.Count > 1) tb.Text = args[1].ToString();
                    tb.TextChanged += (s, e) => InvokeHandler(id, "change", tb.Text);
                    tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) InvokeHandler(id, "enter", tb.Text); };
                    ApplyBaseProps(tb, id, args, 2);
                    _mainForm.Controls.Add(tb);
                });

                while (tb == null) Thread.Sleep(1);
                return new GuiValue(tb);
            });

            builtins["Gui.textarea"] = new BuiltinFunctionValue("Gui.textarea", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "textarea";
                TextBox? tb = null;

                _mainForm?.Invoke(() =>
                {
                    tb = new TextBox
                    {
                        Multiline = true,
                        Width = 300,
                        Height = 150,
                        ScrollBars = ScrollBars.Vertical,
                        BorderStyle = BorderStyle.FixedSingle
                    };
                    if (args.Count > 1) tb.Text = args[1].ToString();
                    tb.TextChanged += (s, e) => InvokeHandler(id, "change", tb.Text);
                    ApplyBaseProps(tb, id, args, 2);
                    _mainForm.Controls.Add(tb);
                });

                while (tb == null) Thread.Sleep(1);
                return new GuiValue(tb);
            });

            builtins["Gui.checkbox"] = new BuiltinFunctionValue("Gui.checkbox", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "chk";
                var text = args.Count > 1 ? args[1].ToString() : "";
                CheckBox? cb = null;

                _mainForm?.Invoke(() =>
                {
                    cb = new CheckBox { Text = text, AutoSize = true };
                    cb.CheckedChanged += (s, e) => InvokeHandler(id, "change", cb.Checked.ToString());
                    ApplyBaseProps(cb, id, args, 2);
                    _mainForm.Controls.Add(cb);
                });

                while (cb == null) Thread.Sleep(1);
                return new GuiValue(cb);
            });

            builtins["Gui.dropdown"] = new BuiltinFunctionValue("Gui.dropdown", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "dropdown";
                ComboBox? cb = null;

                _mainForm?.Invoke(() =>
                {
                    cb = new ComboBox
                    {
                        Width = 200,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        FlatStyle = FlatStyle.Flat
                    };
                    if (args.Count > 1 && args[1] is ArrayValue items)
                        foreach (var item in items.Elements) cb.Items.Add(item.ToString());
                    if (cb.Items.Count > 0) cb.SelectedIndex = 0;
                    cb.SelectedIndexChanged += (s, e) => InvokeHandler(id, "change", cb.SelectedItem?.ToString() ?? "");
                    ApplyBaseProps(cb, id, args, 2);
                    _mainForm.Controls.Add(cb);
                });

                while (cb == null) Thread.Sleep(1);
                return new GuiValue(cb);
            });

            builtins["Gui.slider"] = new BuiltinFunctionValue("Gui.slider", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "slider";
                var min = args.Count > 1 ? (int)args[1].AsNumber() : 0;
                var max = args.Count > 2 ? (int)args[2].AsNumber() : 100;
                TrackBar? tb = null;

                _mainForm?.Invoke(() =>
                {
                    tb = new TrackBar { Minimum = min, Maximum = max, Width = 200, TickStyle = TickStyle.None };
                    tb.ValueChanged += (s, e) => InvokeHandler(id, "change", tb.Value.ToString());
                    ApplyBaseProps(tb, id, args, 3);
                    _mainForm.Controls.Add(tb);
                });

                while (tb == null) Thread.Sleep(1);
                return new GuiValue(tb);
            });

            builtins["Gui.progress"] = new BuiltinFunctionValue("Gui.progress", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "progress";
                var max = args.Count > 1 ? (int)args[1].AsNumber() : 100;
                ProgressBar? pb = null;

                _mainForm?.Invoke(() =>
                {
                    pb = new ProgressBar { Maximum = max, Width = 200, Height = 25, Style = ProgressBarStyle.Continuous };
                    ApplyBaseProps(pb, id, args, 2);
                    _mainForm.Controls.Add(pb);
                });

                while (pb == null) Thread.Sleep(1);
                return new GuiValue(pb);
            });

            builtins["Gui.image"] = new BuiltinFunctionValue("Gui.image", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "img";
                var path = args.Count > 1 ? args[1].ToString() : "";
                PictureBox? pb = null;

                _mainForm?.Invoke(() =>
                {
                    pb = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Width = 200, Height = 200 };
                    try { if (!string.IsNullOrEmpty(path)) pb.Image = Image.FromFile(path); } catch { }
                    ApplyBaseProps(pb, id, args, 2);
                    _mainForm.Controls.Add(pb);
                });

                while (pb == null) Thread.Sleep(1);
                return new GuiValue(pb);
            });

            builtins["Gui.panel"] = new BuiltinFunctionValue("Gui.panel", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "panel";
                Panel? pnl = null;

                _mainForm?.Invoke(() =>
                {
                    pnl = new Panel { Width = 300, Height = 200, BorderStyle = BorderStyle.FixedSingle };
                    ApplyBaseProps(pnl, id, args, 1);
                    _mainForm.Controls.Add(pnl);
                });

                while (pnl == null) Thread.Sleep(1);
                return new GuiValue(pnl);
            });

            builtins["Gui.listbox"] = new BuiltinFunctionValue("Gui.listbox", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "list";
                ListBox? lb = null;

                _mainForm?.Invoke(() =>
                {
                    lb = new ListBox { Width = 200, Height = 150, BorderStyle = BorderStyle.FixedSingle };
                    if (args.Count > 1 && args[1] is ArrayValue items)
                        foreach (var item in items.Elements) lb.Items.Add(item.ToString());
                    lb.SelectedIndexChanged += (s, e) => InvokeHandler(id, "select", lb.SelectedItem?.ToString() ?? "");
                    ApplyBaseProps(lb, id, args, 2);
                    _mainForm.Controls.Add(lb);
                });

                while (lb == null) Thread.Sleep(1);
                return new GuiValue(lb);
            });

            builtins["Gui.tabs"] = new BuiltinFunctionValue("Gui.tabs", (state, args) =>
            {
                var id = args.Count > 0 ? args[0].ToString() : "tabs";
                TabControl? tc = null;

                _mainForm?.Invoke(() =>
                {
                    tc = new TabControl { Width = 400, Height = 300 };
                    if (args.Count > 1 && args[1] is ArrayValue names)
                    {
                        foreach (var name in names.Elements)
                        {
                            var page = new TabPage(name.ToString());
                            tc.TabPages.Add(page);
                            _controls[$"{id}.{name}"] = page;
                        }
                    }
                    tc.SelectedIndexChanged += (s, e) => InvokeHandler(id, "change", tc.SelectedTab?.Text ?? "");
                    ApplyBaseProps(tc, id, args, 2);
                    _mainForm.Controls.Add(tc);
                });

                while (tc == null) Thread.Sleep(1);
                return new GuiValue(tc);
            });

            builtins["Gui.on"] = new BuiltinFunctionValue("Gui.on", (state, args) =>
            {
                if (args.Count < 3) return new NullValue();
                var id = args[0].ToString();
                var evt = args[1].ToString();
                if (args[2] is ICallable handler)
                    _handlers[$"{id}.{evt}"] = handler;
                return new NullValue();
            });

            builtins["Gui.set"] = new BuiltinFunctionValue("Gui.set", (state, args) =>
            {
                if (args.Count < 3) return new NullValue();
                var id = args[0].ToString();
                var prop = args[1].ToString().ToLower();
                var val = args[2];

                if (!_controls.TryGetValue(id, out var ctrl)) return new NullValue();

                ctrl.Invoke(() =>
                {
                    switch (prop)
                    {
                        case "text": ctrl.Text = val.ToString(); break;
                        case "x": ctrl.Left = (int)val.AsNumber(); break;
                        case "y": ctrl.Top = (int)val.AsNumber(); break;
                        case "width": case "w": ctrl.Width = (int)val.AsNumber(); break;
                        case "height": case "h": ctrl.Height = (int)val.AsNumber(); break;
                        case "visible": ctrl.Visible = val.AsBool(); break;
                        case "enabled": ctrl.Enabled = val.AsBool(); break;
                        case "bg": ctrl.BackColor = ParseColor(val.ToString()); break;
                        case "fg": ctrl.ForeColor = ParseColor(val.ToString()); break;
                        case "fontsize": ctrl.Font = new Font(ctrl.Font.FontFamily, (float)val.AsNumber()); break;
                        case "value":
                            if (ctrl is TrackBar slider) slider.Value = Math.Clamp((int)val.AsNumber(), slider.Minimum, slider.Maximum);
                            else if (ctrl is ProgressBar pb) pb.Value = Math.Clamp((int)val.AsNumber(), pb.Minimum, pb.Maximum);
                            else if (ctrl is CheckBox chk) chk.Checked = val.AsBool();
                            else ctrl.Text = val.ToString();
                            break;
                        case "image":
                            if (ctrl is PictureBox pic) try { pic.Image = Image.FromFile(val.ToString()); } catch { }
                            break;
                    }
                });

                return new NullValue();
            });

            builtins["Gui.get"] = new BuiltinFunctionValue("Gui.get", (state, args) =>
            {
                if (args.Count < 2) return new NullValue();
                var id = args[0].ToString();
                var prop = args[1].ToString().ToLower();

                if (!_controls.TryGetValue(id, out var ctrl)) return new NullValue();

                IValue result = new NullValue();
                ctrl.Invoke(() =>
                {
                    result = prop switch
                    {
                        "text" => new StringValue(ctrl.Text),
                        "x" => new NumberValue(ctrl.Left),
                        "y" => new NumberValue(ctrl.Top),
                        "width" or "w" => new NumberValue(ctrl.Width),
                        "height" or "h" => new NumberValue(ctrl.Height),
                        "visible" => new BooleanValue(ctrl.Visible),
                        "enabled" => new BooleanValue(ctrl.Enabled),
                        "value" => ctrl switch
                        {
                            TrackBar tb => new NumberValue(tb.Value),
                            ProgressBar pb => new NumberValue(pb.Value),
                            CheckBox chk => new BooleanValue(chk.Checked),
                            _ => new StringValue(ctrl.Text)
                        },
                        "checked" => ctrl is CheckBox cb ? new BooleanValue(cb.Checked) : new BooleanValue(false),
                        "selected" => ctrl is ListBox lb && lb.SelectedItem != null ? new StringValue(lb.SelectedItem.ToString()!) : new NullValue(),
                        _ => new NullValue()
                    };
                });

                return result;
            });

            builtins["Gui.addTo"] = new BuiltinFunctionValue("Gui.addTo", (state, args) =>
            {
                if (args.Count < 2) return new NullValue();
                var parentId = args[0].ToString();
                var childId = args[1].ToString();

                if (_controls.TryGetValue(parentId, out var parent) && _controls.TryGetValue(childId, out var child))
                    parent.Invoke(() => parent.Controls.Add(child));
                return new NullValue();
            });

            builtins["Gui.remove"] = new BuiltinFunctionValue("Gui.remove", (state, args) =>
            {
                if (args.Count < 1) return new NullValue();
                var id = args[0].ToString();
                if (_controls.TryGetValue(id, out var ctrl))
                {
                    ctrl.Invoke(() => ctrl.Parent?.Controls.Remove(ctrl));
                    _controls.Remove(id);
                }
                return new NullValue();
            });

            builtins["Gui.clear"] = new BuiltinFunctionValue("Gui.clear", (state, args) =>
            {
                if (args.Count < 1) return new NullValue();
                var id = args[0].ToString();
                if (_controls.TryGetValue(id, out var ctrl))
                    ctrl.Invoke(() => ctrl.Controls.Clear());
                return new NullValue();
            });

            builtins["Gui.listAdd"] = new BuiltinFunctionValue("Gui.listAdd", (state, args) =>
            {
                if (args.Count < 2) return new NullValue();
                var id = args[0].ToString();
                var item = args[1].ToString();
                if (_controls.TryGetValue(id, out var ctrl) && ctrl is ListBox lb)
                    lb.Invoke(() => lb.Items.Add(item));
                else if (_controls.TryGetValue(id, out var ctrl2) && ctrl2 is ComboBox cb)
                    cb.Invoke(() => cb.Items.Add(item));
                return new NullValue();
            });

            builtins["Gui.listClear"] = new BuiltinFunctionValue("Gui.listClear", (state, args) =>
            {
                if (args.Count < 1) return new NullValue();
                var id = args[0].ToString();
                if (_controls.TryGetValue(id, out var ctrl) && ctrl is ListBox lb)
                    lb.Invoke(() => lb.Items.Clear());
                else if (_controls.TryGetValue(id, out var ctrl2) && ctrl2 is ComboBox cb)
                    cb.Invoke(() => cb.Items.Clear());
                return new NullValue();
            });

            builtins["Gui.msgbox"] = new BuiltinFunctionValue("Gui.msgbox", (state, args) =>
            {
                var text = args.Count > 0 ? args[0].ToString() : "";
                var title = args.Count > 1 ? args[1].ToString() : "OLLang";
                var type = args.Count > 2 ? args[2].ToString().ToLower() : "info";

                var icon = type switch
                {
                    "error" => MessageBoxIcon.Error,
                    "warn" or "warning" => MessageBoxIcon.Warning,
                    "question" => MessageBoxIcon.Question,
                    _ => MessageBoxIcon.Information
                };

                var result = MessageBox.Show(text, title, type == "question" ? MessageBoxButtons.YesNo : MessageBoxButtons.OK, icon);
                return new BooleanValue(result == DialogResult.Yes || result == DialogResult.OK);
            });

            builtins["Gui.filePicker"] = new BuiltinFunctionValue("Gui.filePicker", (state, args) =>
            {
                var filter = args.Count > 0 ? args[0].ToString() : "All files|*.*";
                string? path = null;
                var t = new Thread(() =>
                {
                    var dlg = new OpenFileDialog { Filter = filter };
                    if (dlg.ShowDialog() == DialogResult.OK) path = dlg.FileName;
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                return path != null ? new StringValue(path) : (IValue)new NullValue();
            });

            builtins["Gui.colorPicker"] = new BuiltinFunctionValue("Gui.colorPicker", (state, args) =>
            {
                string? hex = null;
                var t = new Thread(() =>
                {
                    var dlg = new ColorDialog { FullOpen = true };
                    if (dlg.ShowDialog() == DialogResult.OK)
                        hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                return hex != null ? new StringValue(hex) : (IValue)new NullValue();
            });

            builtins["Gui.timer"] = new BuiltinFunctionValue("Gui.timer", (state, args) =>
            {
                if (args.Count < 2) return new NullValue();
                var ms = (int)args[0].AsNumber();
                var handler = args[1] as ICallable;
                if (handler == null) return new NullValue();
                _state = state;

                _mainForm?.Invoke(() =>
                {
                    var timer = new System.Windows.Forms.Timer { Interval = ms };
                    
                    _mainForm.FormClosed += (s, e) => timer.Stop();
                    
                    timer.Tick += (s, e) =>
                    {
                        try 
                        { 
                            var res = handler.Call(state, new List<IValue>()); 
                            if (res is BooleanValue bv && !bv.Value) timer.Stop();
                            if (res is NullValue) timer.Stop();
                        } 
                        catch { }
                    };
                    timer.Start();
                });

                return new NullValue();
            });

            builtins["Gui.close"] = new BuiltinFunctionValue("Gui.close", (state, args) =>
            {
                _mainForm?.Invoke(() => _mainForm.Close());
                return new NullValue();
            });

            builtins["Gui.alive"] = new BuiltinFunctionValue("Gui.alive", (state, args) => new BooleanValue(_appRunning));

            builtins["Gui.wait"] = new BuiltinFunctionValue("Gui.wait", (state, args) =>
            {
                while (_appRunning) Thread.Sleep(50);
                return new NullValue();
            });
        }
    }
}
