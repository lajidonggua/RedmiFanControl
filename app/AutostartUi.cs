using System;
using System.Drawing;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm {
        CheckBox startupBox;IStartupStore startupStore;bool updatingStartup;
        internal bool StartInTray;
        void InitializeAutostart(Control header){
            startupStore=preview?(IStartupStore)new MemoryStartupStore():new RegistryStartupStore();
            startupBox=new CheckBox{Text="登录后启动到托盘",Location=new Point(715,44),Size=new Size(190,24),Font=new Font("Microsoft YaHei UI",8),ForeColor=Muted};header.Controls.Add(startupBox);
            try{startupBox.Checked=startupStore.Read()!=null;}catch(Exception ex){hints.SetToolTip(startupBox,"无法读取自启设置："+ex.Message);}
            hints.SetToolTip(startupBox,"仅当前用户登录后启动到托盘，不自动提权、不自动接管风扇。需要监测/调速时点击连接并确认 UAC。开启后请勿移动程序文件夹；更新版本后可重新勾选以更新路径。");
            startupBox.CheckedChanged+=delegate{
                if(updatingStartup)return;bool requested=startupBox.Checked;
                try{Autostart.Set(startupStore,Application.ExecutablePath,requested);startupBox.Text=requested?"已启用登录自启":"开机自启（登录后）";}
                catch(Exception ex){updatingStartup=true;startupBox.Checked=!requested;updatingStartup=false;if(!preview)MessageBox.Show(this,ex.Message,"自启设置失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            };
        }
        void ShowMain(){ShowInTaskbar=true;Show();WindowState=FormWindowState.Normal;Activate();}
        internal void VerifyAutostartUi(){
            if(!preview)throw new InvalidOperationException("Autostart test must use memory only.");
            startupBox.Checked=true;if(startupStore.Read()!=Autostart.Command(Application.ExecutablePath))throw new Exception("Startup checkbox enable");
            startupBox.Checked=false;if(startupStore.Read()!=null)throw new Exception("Startup checkbox disable");
        }
    }
}
