using System;
using System.Drawing;
using System.Windows.Forms;

namespace ChargeGuard
{
    public class SettingsForm : Form
    {
        private readonly AppConfig _working;
        private NumericUpDown _limit;
        private NumericUpDown _resume;
        private RadioButton _monitorMode;
        private RadioButton _plugMode;
        private TextBox _onUrl;
        private TextBox _offUrl;
        private CheckBox _autostart;
        private CheckBox _notifications;
        private CheckBox _restoreOnExit;
        private Label _statusLabel;

        public AppConfig ResultConfig { get { return _working; } }

        public SettingsForm(AppConfig config, BatteryStatus status)
        {
            _working = config.Clone();

            SuspendLayout();
            Text = "ChargeGuard 设置";
            ClientSize = new Size(560, 612);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Label title = new Label();
            title.Text = "ChargeGuard · 充电上限控制";
            title.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            title.SetBounds(16, 10, 520, 30);
            Controls.Add(title);

            _statusLabel = new Label();
            _statusLabel.SetBounds(18, 42, 520, 20);
            _statusLabel.ForeColor = Color.FromArgb(90, 90, 90);
            Controls.Add(_statusLabel);

            GroupBox limitGroup = MakeGroup("充电限制", 14, 70, 530, 112);
            Controls.Add(limitGroup);

            Label limitLabel = MakeLabel("充电上限", 14, 25, 82, 22);
            limitGroup.Controls.Add(limitLabel);

            _limit = new NumericUpDown();
            _limit.SetBounds(100, 22, 70, 25);
            _limit.Minimum = 50;
            _limit.Maximum = 100;
            _limit.Value = _working.Limit;
            limitGroup.Controls.Add(_limit);

            Label limitHint = MakeLabel("%   达到该电量后停止充电", 178, 25, 330, 22);
            limitHint.ForeColor = Color.FromArgb(90, 90, 90);
            limitGroup.Controls.Add(limitHint);

            Label resumeLabel = MakeLabel("恢复阈值", 14, 63, 82, 22);
            limitGroup.Controls.Add(resumeLabel);

            _resume = new NumericUpDown();
            _resume.SetBounds(100, 60, 70, 25);
            _resume.Minimum = 40;
            _resume.Maximum = 99;
            _resume.Value = _working.Resume;
            limitGroup.Controls.Add(_resume);

            Label resumeHint = MakeLabel("%   低于该值重新供电，必须小于上限", 178, 63, 330, 22);
            resumeHint.ForeColor = Color.FromArgb(90, 90, 90);
            limitGroup.Controls.Add(resumeHint);

            GroupBox modeGroup = MakeGroup("执行方式", 14, 190, 530, 202);
            Controls.Add(modeGroup);

            _monitorMode = new RadioButton();
            _monitorMode.Text = "仅监控提醒（不改动供电，无法真正停止充电）";
            _monitorMode.SetBounds(14, 24, 490, 24);
            _monitorMode.Checked = !_working.IsPlugMode;
            modeGroup.Controls.Add(_monitorMode);

            _plugMode = new RadioButton();
            _plugMode.Text = "智能插座（达到上限时通过 HTTP 切断充电器电源）";
            _plugMode.SetBounds(14, 52, 490, 24);
            _plugMode.Checked = _working.IsPlugMode;
            modeGroup.Controls.Add(_plugMode);

            Label onLabel = MakeLabel("开插座 URL", 14, 85, 100, 22);
            modeGroup.Controls.Add(onLabel);

            _onUrl = new TextBox();
            _onUrl.SetBounds(118, 82, 396, 25);
            _onUrl.Text = _working.PlugOnUrl;
            modeGroup.Controls.Add(_onUrl);

            Label offLabel = MakeLabel("关插座 URL", 14, 116, 100, 22);
            modeGroup.Controls.Add(offLabel);

            _offUrl = new TextBox();
            _offUrl.SetBounds(118, 113, 396, 25);
            _offUrl.Text = _working.PlugOffUrl;
            modeGroup.Controls.Add(_offUrl);

            Label example = new Label();
            example.Text = "Tasmota: http://IP/cm?cmnd=Power%20On  ／  Power%20Off\r\nShelly:   http://IP/relay/0?turn=on  ／  turn=off\r\n插座仅连接充电器；程序未运行、睡眠或关机时不执行控制。";
            example.SetBounds(118, 142, 396, 46);
            example.Font = new Font("Microsoft YaHei UI", 8F);
            example.ForeColor = Color.FromArgb(110, 110, 110);
            modeGroup.Controls.Add(example);

            GroupBox optionGroup = MakeGroup("选项", 14, 400, 530, 110);
            Controls.Add(optionGroup);

            _autostart = new CheckBox();
            _autostart.Text = "开机自动运行（当前用户）";
            _autostart.SetBounds(14, 22, 300, 24);
            _autostart.Checked = _working.Autostart;
            optionGroup.Controls.Add(_autostart);

            _notifications = new CheckBox();
            _notifications.Text = "显示气泡通知";
            _notifications.SetBounds(14, 49, 300, 24);
            _notifications.Checked = _working.Notifications;
            optionGroup.Controls.Add(_notifications);

            _restoreOnExit = new CheckBox();
            _restoreOnExit.Text = "退出程序时恢复插座供电（建议开启）";
            _restoreOnExit.SetBounds(14, 76, 490, 24);
            _restoreOnExit.Checked = _working.RestoreOnExit;
            optionGroup.Controls.Add(_restoreOnExit);

            Label note = new Label();
            note.Text = "硬件说明：ONE-NETBOOK OXP2 没有公开的 Windows 充电阈值接口。本程序不写入 EC，避免损坏硬件。监控模式只作提醒，真正断电需要智能插座。";
            note.SetBounds(16, 518, 528, 44);
            note.ForeColor = Color.FromArgb(150, 90, 40);
            Controls.Add(note);

            Button save = new Button();
            save.Text = "保存";
            save.SetBounds(340, 568, 95, 30);
            save.UseVisualStyleBackColor = true;
            save.Click += SaveClicked;
            Controls.Add(save);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(445, 568, 95, 30);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.UseVisualStyleBackColor = true;
            Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;

            _plugMode.CheckedChanged += delegate { UpdateUrlState(); };
            UpdateUrlState();

            _statusLabel.Text = "当前：" + status.Describe();
            ResumeLayout(false);
        }

        private static GroupBox MakeGroup(string text, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            g.SetBounds(x, y, w, h);
            return g;
        }

        private static Label MakeLabel(string text, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        private void UpdateUrlState()
        {
            bool on = _plugMode.Checked;
            _onUrl.Enabled = on;
            _offUrl.Enabled = on;
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            int limit = (int)_limit.Value;
            int resume = (int)_resume.Value;

            if (resume >= limit)
            {
                MessageBox.Show(this, "恢复阈值必须小于充电上限。", "设置无效",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _resume.Focus();
                return;
            }

            string onUrl = _onUrl.Text.Trim();
            string offUrl = _offUrl.Text.Trim();
            bool usePlug = _plugMode.Checked;
            if (usePlug)
            {
                if (!AppConfig.IsHttpUrl(onUrl) || !AppConfig.IsHttpUrl(offUrl))
                {
                    MessageBox.Show(this, "智能插座模式需要两个有效的 http:// 或 https:// 地址。",
                        "设置无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult confirm = MessageBox.Show(this,
                    "请确认：插座上只能连接充电器，不能连接其他重要设备。\r\n\r\n继续保存？",
                    "确认智能插座", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;
            }

            _working.Limit = limit;
            _working.Resume = resume;
            _working.Mode = usePlug ? "SmartPlug" : "Monitor";
            _working.PlugOnUrl = onUrl;
            _working.PlugOffUrl = offUrl;
            _working.Autostart = _autostart.Checked;
            _working.Notifications = _notifications.Checked;
            _working.RestoreOnExit = _restoreOnExit.Checked;
            _working.Normalize();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
