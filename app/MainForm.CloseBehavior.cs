using System;
using System.Drawing;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm {
        void LoadUiPreferences(){
            string note;
            uiPreferences=preview?new UiPreferences():UiPreferencesStore.LoadDefault(out note);
        }
        void SaveUiPreferences(){
            if(preview || uiPreferences==null)return;
            try{UiPreferencesStore.Save(UiPreferencesStore.DefaultPath,uiPreferences);}
            catch(Exception ex){hints.SetToolTip(status,"界面偏好保存失败："+ex.Message);}
        }
        internal void HideToTray(){
            if(uiPreferences==null)uiPreferences=new UiPreferences();
            uiPreferences.LastInTray=true;SaveUiPreferences();
            ShowInTaskbar=false;if(tray!=null)tray.Visible=true;Hide();
        }
        void MarkVisible(){
            if(uiPreferences==null)uiPreferences=new UiPreferences();
            uiPreferences.LastInTray=false;SaveUiPreferences();ShowInTaskbar=true;
        }
        void HandleUserClose(){
            CloseBehavior behavior=uiPreferences==null?CloseBehavior.Ask:uiPreferences.CloseBehavior;
            bool remember=false;
            if(behavior==CloseBehavior.Ask){
                using(var prompt=new ClosePrompt(uiPreferences==null?CloseBehavior.HideToTray:uiPreferences.CloseBehavior)){
                    if(prompt.ShowDialog(this)!=DialogResult.OK)return;
                    behavior=prompt.Behavior;remember=prompt.Remember;
                }
                if(remember){uiPreferences.CloseBehavior=behavior;SaveUiPreferences();}
            }
            if(behavior==CloseBehavior.HideToTray){HideToTray();return;}
            BeginShutdownFromUser();
        }
        static string CloseBehaviorText(CloseBehavior behavior){
            return behavior==CloseBehavior.HideToTray?"隐藏到系统托盘（后台继续运行）":behavior==CloseBehavior.Exit?"退出程序（恢复系统自动）":"每次点击 × 时询问";
        }
        sealed class ClosePrompt : Form {
            readonly RadioButton hide,exit;readonly CheckBox remember;
            public CloseBehavior Behavior{get;private set;}
            public bool Remember{get{return remember.Checked;}}
            public ClosePrompt(CloseBehavior defaultBehavior){
                Text="关闭 Redmi Fan Control";Icon=AppBrand.WindowIcon();StartPosition=FormStartPosition.CenterParent;
                FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
                ClientSize=new Size(500,282);Font=new Font("Microsoft YaHei UI",9);BackColor=Color.White;
                AddLabel(this,"要如何处理当前窗口？",24,20,440,30,14,Ink,true);
                AddLabel(this,"直接点击 × 容易误退出。隐藏到托盘会继续监测/调速；退出前会等待恢复系统自动。",24,57,450,42,9,Muted,false);
                hide=new RadioButton{Text="隐藏到系统托盘，后台继续运行",Location=new Point(28,112),Size=new Size(420,28),Checked=true};Controls.Add(hide);
                AddLabel(this,"下次可从托盘图标打开主窗口。",50,137,400,22,8,Muted,false);
                exit=new RadioButton{Text="退出程序，并恢复系统自动调度",Location=new Point(28,167),Size=new Size(420,28)};Controls.Add(exit);
                AddLabel(this,"退出会等待后台确认风扇已交回系统。",50,192,400,22,8,Muted,false);
                remember=new CheckBox{Text="记住我的选择，下次点击 × 不再询问",Location=new Point(28,221),Size=new Size(420,24)};Controls.Add(remember);
                var cancel=Button(this,"取消",292,252,82,30,false);cancel.DialogResult=DialogResult.Cancel;
                var ok=Button(this,"执行",384,252,88,30,true);ok.Click+=delegate{Behavior=hide.Checked?CloseBehavior.HideToTray:CloseBehavior.Exit;DialogResult=DialogResult.OK;Close();};
                AcceptButton=ok;CancelButton=cancel;Behavior=CloseBehavior.HideToTray;
                if(defaultBehavior==CloseBehavior.Exit){hide.Checked=false;exit.Checked=true;Behavior=CloseBehavior.Exit;}
                Disposed+=delegate{Icon.Dispose();};UiScale.Apply(this,new Size(500,282),new Size(500,282));
            }
        }
    }
}
