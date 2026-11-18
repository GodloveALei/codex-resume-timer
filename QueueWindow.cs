using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class ResumeTimerForm : Form
{
    internal static readonly Color Paper = Color.FromArgb(248, 245, 255);
    internal static readonly Color Ink = Color.FromArgb(45, 33, 72);
    internal static readonly Color Muted = Color.FromArgb(98, 82, 126);
    internal static readonly Color Violet = Color.FromArgb(97, 62, 171);
    private static readonly Color Lavender = Color.FromArgb(231, 219, 255);

    private readonly TextBox link = new TextBox { MaxLength = 4096, Dock = DockStyle.Fill };
    private readonly TextBox alias = new TextBox { MaxLength = 80, Dock = DockStyle.Fill };
    private readonly NumericUpDown hours = Number(168, 1, "延迟小时数");
    private readonly NumericUpDown minutes = Number(59, 0, "延迟分钟数");
    private readonly CheckBox keepAwake = new CheckBox { Text = "有定时时防止自动睡眠", Checked = true, AutoSize = true };
    private readonly Button add = MakeButton("添加定时", Color.White, Violet);
    private readonly Button paste = MakeButton("粘贴", Violet, Color.FromArgb(242, 234, 255));
    private readonly Label notice = LabelText("", 9F, Muted);
    private readonly Label summary = LabelText("", 9F, Muted);
    private readonly Label powerNote = LabelText("", 9F, Muted);
    private readonly Label empty = LabelText("列表还是空的\n添加对话和延迟后，可在这里查看倒计时。", 10.5F, Muted);
    private readonly TableLayoutPanel list = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
    private readonly ToolTip tips = new ToolTip { AutoPopDelay = 12000 };
    private readonly Timer clock = new Timer { Interval = 250 };
    private readonly List<ScheduleRow> jobs = new List<ScheduleRow>();
    private bool binding;
    private bool sleepInhibited;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint flags);

    public ResumeTimerForm()
    {
        Text = "Codex 定时继续";
        Font = new Font("Microsoft YaHei UI", 9.5F);
        ForeColor = Ink;
        BackColor = Paper;
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(836, 600);
        Size = MinimumSize;
        using (var icon = typeof(ResumeTimerForm).Assembly.GetManifestResourceStream("CodexResumeTimer.ico"))
            Icon = new Icon(icon);

        var page = Grid(1, new Padding(21, 16, 21, 12));
        page.Dock = DockStyle.Fill;
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new RoundedPanel(Lavender, 22) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        var banner = Grid(2, new Padding(22, 10, 12, 10));
        banner.Dock = DockStyle.Fill;
        banner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        banner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        var words = Grid(1, Padding.Empty);
        words.Dock = DockStyle.Fill;
        words.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        words.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        var title = LabelText("Codex 定时继续", 22F, Ink, FontStyle.Bold);
        title.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        words.Controls.Add(title, 0, 0);
        words.Controls.Add(LabelText("为每个对话单独设好时间，到点发出「继续」。", 10F, Muted), 0, 1);
        banner.Controls.Add(words, 0, 0);
        banner.Controls.Add(new AlarmIllustration { Dock = DockStyle.Fill }, 1, 0);
        header.Controls.Add(banner);
        page.Controls.Add(header, 0, 0);

        var work = Grid(2, Padding.Empty);
        work.Dock = DockStyle.Fill;
        work.Margin = Padding.Empty;
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 304));
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var addPanel = new RoundedPanel(Color.White, 17) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        addPanel.Controls.Add(BuildEntry());
        work.Controls.Add(addPanel, 0, 0);
        var listPanel = new RoundedPanel(Color.White, 17) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        listPanel.Controls.Add(BuildList());
        work.Controls.Add(listPanel, 1, 0);
        page.Controls.Add(work, 0, 1);

        var footer = Flow(keepAwake, powerNote);
        footer.Margin = new Padding(0, 8, 0, 0);
        footer.Padding = new Padding(5, 3, 5, 0);
        page.Controls.Add(footer, 0, 2);

        Controls.Add(page);
        AcceptButton = add;
        paste.Click += (sender, args) => PasteLink();
        add.Click += async (sender, args) => await AddAsync();
        keepAwake.CheckedChanged += (sender, args) => UpdateSleepRequest();
        clock.Tick += (sender, args) => Tick();
        UpdateQueue();
        clock.Start();
    }

    private Control BuildEntry()
    {
        var entry = Grid(1, new Padding(16, 14, 16, 12));
        entry.Dock = DockStyle.Fill;
        entry.AutoScroll = true;
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = LabelText("添加对话", 15F, Ink, FontStyle.Bold);
        heading.Margin = new Padding(0, 1, 0, 12);
        entry.Controls.Add(heading);
        entry.Controls.Add(LabelText("对话链接或 ID", 9.5F, Ink, FontStyle.Bold));

        var linkRow = Grid(2, Padding.Empty);
        linkRow.AutoSize = true;
        linkRow.Dock = DockStyle.Top;
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 59));
        link.AccessibleName = "Codex 对话链接或 ID";
        link.Margin = new Padding(0, 8, 8, 5);
        paste.Dock = DockStyle.Fill;
        paste.Margin = Padding.Empty;
        paste.Height = 34;
        linkRow.Controls.Add(link, 0, 0);
        linkRow.Controls.Add(paste, 1, 0);
        entry.Controls.Add(linkRow);
        var instruction = LabelText("在目标对话按 Ctrl+Alt+L 复制链接", 8.8F, Muted);
        instruction.Margin = new Padding(0, 4, 0, 18);
        entry.Controls.Add(instruction);

        entry.Controls.Add(LabelText("备注（选填）", 9.5F, Ink, FontStyle.Bold));
        alias.AccessibleName = "对话备注（选填）";
        alias.Margin = new Padding(0, 5, 0, 17);
        entry.Controls.Add(alias);
        entry.Controls.Add(LabelText("延迟发送", 9.5F, Ink, FontStyle.Bold));
        var delay = Flow(hours, LabelText("小时", 9.5F, Muted), minutes, LabelText("分钟", 9.5F, Muted));
        delay.Margin = new Padding(0, 3, 0, 13);
        entry.Controls.Add(delay);
        add.Dock = DockStyle.Top;
        add.Height = 40;
        add.Margin = new Padding(0, 0, 0, 10);
        entry.Controls.Add(add);
        notice.MaximumSize = new Size(252, 0);
        notice.AccessibleRole = AccessibleRole.StaticText;
        entry.Controls.Add(notice);
        return entry;
    }

    private Control BuildList()
    {
        var schedules = Grid(1, new Padding(15, 14, 14, 10));
        schedules.Dock = DockStyle.Fill;
        schedules.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        schedules.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var legend = Grid(2, Padding.Empty);
        legend.AutoSize = true;
        legend.Dock = DockStyle.Top;
        legend.Margin = new Padding(2, 0, 5, 10);
        legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        legend.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        legend.Controls.Add(LabelText("定时列表", 15F, Ink, FontStyle.Bold), 0, 0);
        summary.Anchor = AnchorStyles.Right;
        legend.Controls.Add(summary, 1, 0);
        schedules.Controls.Add(legend, 0, 0);

        var scroll = new Panel { AutoScroll = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        empty.Padding = new Padding(16, 22, 16, 22);
        empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.Dock = DockStyle.Top;
        empty.AutoSize = false;
        empty.Height = 96;
        empty.BackColor = Paper;
        scroll.Controls.Add(list);
        scroll.Controls.Add(empty);
        schedules.Controls.Add(scroll, 0, 1);
        return schedules;
    }

    private void PasteLink()
    {
        try
        {
            link.Text = Clipboard.GetText().Trim();
            link.Focus();
        }
        catch (ExternalException)
        {
            SetNotice("暂时无法读剪贴板，请试用 Ctrl+V。", true);
        }
    }

    private async Task AddAsync()
    {
        if (binding)
            return;

        try
        {
            ChatTarget target = ChatTarget.Parse(link.Text);
            var delay = TimeSpan.FromMinutes((double)(hours.Value * 60 + minutes.Value));
            if (delay <= TimeSpan.Zero)
                throw new ArgumentException("请至少设置 1 分钟延迟。");
            if (jobs.Any(job => job.IsActive && job.Target.Link == target.Link))
                throw new ArgumentException("此对话已有定时。可删除原定时后重新添加。");

            string name = alias.Text.Trim();
            binding = true;
            add.Enabled = paste.Enabled = false;
            link.ReadOnly = alias.ReadOnly = true;
            hours.Enabled = minutes.Enabled = false;
            SetNotice("正在验证目标对话…", false);
            using (var desktop = new DesktopConnection())
                await desktop.CheckAsync(target);
            if (IsDisposed)
                return;

            var row = new ScheduleRow(target, name, DateTimeOffset.UtcNow.Add(delay), tips, Remove);
            jobs.Add(row);
            list.Controls.Add(row);
            UpdateQueue();
            link.Clear();
            alias.Clear();
            SetNotice("已添加，可继续添加其他对话。", false);
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                SetNotice(error.Message, true);
        }
        finally
        {
            binding = false;
            if (!IsDisposed)
            {
                add.Enabled = paste.Enabled = true;
                link.ReadOnly = alias.ReadOnly = false;
                hours.Enabled = minutes.Enabled = true;
            }
        }
    }

    private void Remove(ScheduleRow row)
    {
        if (row.State == ScheduleState.Sending || !jobs.Remove(row))
            return;
        list.Controls.Remove(row);
        row.Dispose();
        UpdateQueue();
    }

    private void Tick()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var row in jobs.ToArray())
        {
            if (row.State != ScheduleState.Waiting)
                continue;
            if (row.SendAt > now)
                row.UpdateCountdown(now);
            else
                StartSend(row);
        }
    }

    private async void StartSend(ScheduleRow row)
    {
        row.SetState(ScheduleState.Sending, "正在向这个对话提交「继续」。");
        UpdateQueue();
        DeliveryReceipt result;
        try
        {
            using (var desktop = new DesktopConnection())
                result = await desktop.SendAsync(row.Target);
        }
        catch (Exception error)
        {
            result = new DeliveryReceipt(DeliveryState.Failed, error.Message);
        }

        if (!IsDisposed)
        {
            row.ShowReceipt(result);
            UpdateQueue();
        }
    }

    private void UpdateQueue()
    {
        empty.Visible = jobs.Count == 0;
        int waiting = jobs.Count(job => job.State == ScheduleState.Waiting);
        int sending = jobs.Count(job => job.State == ScheduleState.Sending);
        summary.Text = jobs.Count == 0 ? "0 项" :
            waiting + " 项等待 · " + sending + " 项发送中";
        UpdateSleepRequest();
    }

    private void UpdateSleepRequest()
    {
        bool needed = keepAwake.Checked && jobs.Any(job => job.IsActive);
        if (needed && !sleepInhibited)
            sleepInhibited = SetThreadExecutionState(0x80000001) != 0;
        if (!needed)
            ReleaseSleepRequest();
        powerNote.Text = needed && !sleepInhibited ? "Windows 未能启用防睡眠" : "请保持目标对话打开，可最小化或锁屏";
    }

    private void ReleaseSleepRequest()
    {
        if (sleepInhibited)
        {
            SetThreadExecutionState(0x80000000);
            sleepInhibited = false;
        }
    }

    private void SetNotice(string message, bool error)
    {
        notice.ForeColor = error ? Color.FromArgb(151, 47, 67) : Muted;
        notice.Text = message;
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (args.CloseReason == CloseReason.UserClosing && jobs.Any(job => job.IsActive))
        {
            string message = jobs.Any(job => job.State == ScheduleState.Sending)
                ? "关闭会取消等待中的定时。发送中的对话请在 Codex 核对。"
                : "关闭会取消所有等待中的定时。";
            args.Cancel = MessageBox.Show(this, message, Text, MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK;
        }

        base.OnFormClosing(args);
        if (!args.Cancel)
        {
            ReleaseSleepRequest();
            clock.Dispose();
            tips.Dispose();
        }
    }

    internal static Label LabelText(string text, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text, Font = new Font("Microsoft YaHei UI", size, style),
            ForeColor = color, BackColor = Color.Transparent, AutoSize = true, Margin = new Padding(0, 3, 0, 3),
            UseMnemonic = false
        };
    }

    internal static Button MakeButton(string text, Color color, Color background)
    {
        var button = new Button
        {
            Text = text, ForeColor = color, BackColor = background, FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false, UseMnemonic = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(background, 0.24F);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(background, 0.08F);
        return button;
    }

    private static NumericUpDown Number(int max, int value, string name)
    {
        return new NumericUpDown
        {
            Minimum = 0, Maximum = max, Value = value, Width = 65, TextAlign = HorizontalAlignment.Center,
            AccessibleName = name, Margin = new Padding(0, 0, 7, 0)
        };
    }

    internal static TableLayoutPanel Grid(int columns, Padding padding)
    {
        return new TableLayoutPanel { ColumnCount = columns, BackColor = Color.Transparent, Padding = padding, Margin = Padding.Empty };
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = Padding.Empty };
        panel.Controls.AddRange(controls);
        return panel;
    }
}

internal enum ScheduleState { Waiting, Sending, Delivered, Submitted, Failed, Unconfirmed }

internal sealed class ScheduleRow : RoundedPanel
{
    private static readonly Color WaitingBackground = Color.FromArgb(240, 233, 255);
    private static readonly Color WaitingText = Color.FromArgb(86, 55, 148);
    private readonly Label status = ResumeTimerForm.LabelText("", 10.5F, WaitingText, FontStyle.Bold);
    private readonly Label detail = ResumeTimerForm.LabelText("", 8.8F, ResumeTimerForm.Muted);
    private readonly Button delete = ResumeTimerForm.MakeButton("删除", ResumeTimerForm.Violet, Color.White);
    private readonly RoundedPanel pill = new RoundedPanel(WaitingBackground, 12);
    private readonly ToolTip tips;
    internal readonly ChatTarget Target;
    internal DateTimeOffset SendAt { get; private set; }
    internal ScheduleState State { get; private set; }
    internal bool IsActive { get { return State == ScheduleState.Waiting || State == ScheduleState.Sending; } }

    public ScheduleRow(ChatTarget target, string name, DateTimeOffset sendAt, ToolTip tooltips, Action<ScheduleRow> remove)
        : base(Color.FromArgb(249, 245, 255), 14)
    {
        Target = target;
        SendAt = sendAt;
        tips = tooltips;
        string shortId = target.Id.Substring(target.Id.Length - 8);
        string display = name.Length == 0 ? "对话 " + shortId : name;
        Margin = new Padding(0, 0, 5, 9);
        Dock = DockStyle.Top;
        Height = 79;

        var line = ResumeTimerForm.Grid(3, new Padding(11, 6, 9, 6));
        line.Dock = DockStyle.Fill;
        line.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        var labels = ResumeTimerForm.Grid(1, Padding.Empty);
        labels.Dock = DockStyle.Fill;
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        var title = ResumeTimerForm.LabelText(display, 10.8F, ResumeTimerForm.Ink, FontStyle.Bold);
        title.AutoSize = false;
        title.AutoEllipsis = true;
        title.TextAlign = ContentAlignment.BottomLeft;
        title.Dock = DockStyle.Fill;
        title.Margin = new Padding(0, 0, 5, 0);
        detail.AutoSize = false;
        detail.AutoEllipsis = true;
        detail.TextAlign = ContentAlignment.TopLeft;
        detail.Dock = DockStyle.Fill;
        detail.Margin = new Padding(0, 4, 5, 0);
        labels.Controls.Add(title, 0, 0);
        labels.Controls.Add(detail, 0, 1);
        tips.SetToolTip(title, display + "\n" + target.Link);
        line.Controls.Add(labels, 0, 0);

        pill.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        pill.Margin = new Padding(0, 0, 8, 0);
        pill.Height = 31;
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.AutoSize = false;
        pill.Controls.Add(status);
        line.Controls.Add(pill, 1, 0);

        delete.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        delete.Height = 31;
        delete.Margin = Padding.Empty;
        delete.AccessibleName = "删除 " + display + " 的定时";
        delete.Click += (sender, args) => remove(this);
        line.Controls.Add(delete, 2, 0);
        Controls.Add(line);

        SetState(ScheduleState.Waiting, "计划 " + sendAt.ToLocalTime().ToString("MM-dd HH:mm:ss"));
        UpdateCountdown(DateTimeOffset.UtcNow);
    }

    public void UpdateCountdown(DateTimeOffset now)
    {
        long seconds = Math.Max(0, (long)Math.Ceiling((SendAt - now).TotalSeconds));
        status.Text = String.Format("{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60);
    }

    public void ShowReceipt(DeliveryReceipt receipt)
    {
        switch (receipt.State)
        {
            case DeliveryState.Delivered: SetState(ScheduleState.Delivered, receipt.Detail); break;
            case DeliveryState.Submitted: SetState(ScheduleState.Submitted, receipt.Detail); break;
            case DeliveryState.Failed: SetState(ScheduleState.Failed, receipt.Detail); break;
            default: SetState(ScheduleState.Unconfirmed, receipt.Detail); break;
        }
    }

    public void SetState(ScheduleState state, string message)
    {
        State = state;
        detail.Text = message.Replace("\r", " ").Replace("\n", " ");
        tips.SetToolTip(detail, message);
        delete.Enabled = state != ScheduleState.Sending;
        switch (state)
        {
            case ScheduleState.Sending:
                SetBadge("发送中", Color.FromArgb(223, 237, 253), Color.FromArgb(38, 76, 124));
                break;
            case ScheduleState.Delivered:
                SetBadge("已送达", Color.FromArgb(217, 245, 229), Color.FromArgb(23, 91, 60));
                break;
            case ScheduleState.Submitted:
                SetBadge("已提交", Color.FromArgb(217, 245, 229), Color.FromArgb(23, 91, 60));
                break;
            case ScheduleState.Failed:
                SetBadge("未提交", Color.FromArgb(255, 226, 234), Color.FromArgb(130, 43, 67));
                break;
            case ScheduleState.Unconfirmed:
                SetBadge("待核对", Color.FromArgb(255, 238, 204), Color.FromArgb(105, 67, 19));
                break;
            default:
                SetBadge("", WaitingBackground, WaitingText);
                break;
        }
    }

    private void SetBadge(string text, Color background, Color color)
    {
        status.Text = text;
        status.ForeColor = color;
        pill.Fill = background;
        pill.Invalidate(true);
    }
}

internal class RoundedPanel : Panel
{
    private readonly int radius;
    internal Color Fill;

    public RoundedPanel(Color color, int corner)
    {
        Fill = color;
        radius = corner;
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using (var brush = new SolidBrush(Fill))
        using (var border = new GraphicsPath())
        {
            float d = Math.Min(radius * 2, Math.Min(Width, Height));
            if (d <= 0)
                return;
            border.AddArc(0, 0, d, d, 180, 90);
            border.AddArc(Width - d, 0, d, d, 270, 90);
            border.AddArc(Width - d, Height - d, d, d, 0, 90);
            border.AddArc(0, Height - d, d, d, 90, 90);
            border.CloseFigure();
            var smoothing = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, border);
            e.Graphics.SmoothingMode = smoothing;
        }
    }
}

internal sealed class AlarmIllustration : Control
{
    public AlarmIllustration()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Min(Width / 142F, Height / 84F);
        if (scale <= 0)
            return;
        g.TranslateTransform((Width - 142 * scale) / 2, (Height - 84 * scale) / 2);
        g.ScaleTransform(scale, scale);
        Color outline = Color.FromArgb(58, 40, 98);
        Color yellow = Color.FromArgb(255, 212, 126);
        using (var ink = new Pen(outline, 2.8F))
        using (var hands = new Pen(outline, 2.1F))
        using (var accent = new Pen(Color.FromArgb(134, 94, 207), 2.2F))
        {
            ink.StartCap = ink.EndCap = hands.StartCap = hands.EndCap = accent.StartCap = accent.EndCap = LineCap.Round;
            Circle(g, null, Color.FromArgb(208, 190, 249), 19, 71, 105, 12);
            g.DrawLine(ink, 48, 63, 42, 75);
            g.DrawLine(ink, 95, 63, 101, 75);
            g.DrawLine(ink, 71, 14, 71, 21);
            g.DrawLine(ink, 66, 13, 76, 13);
            Circle(g, ink, Color.FromArgb(255, 174, 153), 31, 11, 24, 15);
            Circle(g, ink, Color.FromArgb(255, 174, 153), 88, 11, 24, 15);
            Circle(g, ink, yellow, 40, 17, 63, 58);
            Circle(g, hands, Color.FromArgb(255, 249, 223), 48, 25, 47, 42);
            g.DrawLine(hands, 72, 40, 72, 31);
            g.DrawLine(hands, 72, 40, 79, 36);
            Circle(g, null, outline, 59, 45, 4, 5);
            Circle(g, null, outline, 80, 45, 4, 5);
            Circle(g, null, Color.FromArgb(255, 183, 161), 53, 51, 8, 4);
            Circle(g, null, Color.FromArgb(255, 183, 161), 82, 51, 8, 4);
            g.DrawArc(hands, 67, 47, 10, 8, 12, 156);
            g.DrawLine(accent, 19, 38, 19, 53);
            g.DrawLine(accent, 12, 45, 26, 45);
            g.DrawLine(accent, 122, 27, 122, 39);
            g.DrawLine(accent, 116, 33, 128, 33);
            Circle(g, null, Color.FromArgb(133, 95, 211), 114, 52, 4, 4);
        }
        g.ResetTransform();
    }

    private static void Circle(Graphics g, Pen stroke, Color color, float x, float y, float width, float height)
    {
        using (var brush = new SolidBrush(color))
            g.FillEllipse(brush, x, y, width, height);
        if (stroke != null)
            g.DrawEllipse(stroke, x, y, width, height);
    }
}
