using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
namespace RedmiFanControl {
    public sealed partial class MainForm {
        bool finishClosing,mayOwnHardware,recoveryConfirmed;
        readonly Stopwatch shutdownAge=new Stopwatch();
        static bool ExitRecoveryConfirmed(Frame frame,string expectedSession){return Presentation.ExitConfirmed(frame,expectedSession);}
        void RequestExit(){directCloseRequested=true;Close();}
        void ClosingSafe(object sender,FormClosingEventArgs args){
            if(preview || finishClosing)return;
            args.Cancel=true;if(closing)return;
            if(args.CloseReason==CloseReason.UserClosing && !directCloseRequested){
                HandleUserClose();return;
            }
            BeginShutdown();
        }
        void BeginShutdownFromUser(){directCloseRequested=true;BeginShutdown();}
        void BeginShutdown(){
            closing=true;RefreshActions();
            if(uiPreferences!=null){uiPreferences.LastInTray=false;SaveUiPreferences();}
            if(worker==null){FinishClose();return;}
            shutdownAge.Restart();
            try{Files.Write(Path.Combine(run,"command.json"),new Message{Session=session,Sequence=++sequence,Command="QUIT"});}
            catch(Exception ex){status.Text="正在结束会话："+ex.Message;}
            timer.Start();
        }
        void CheckShutdown(){
            if(!closing || finishClosing)return;
            ReadRecoveryConfirmation();
            if(recoveryConfirmed || (worker!=null && worker.HasExited && !mayOwnHardware)){FinishClose();return;}
            if(shutdownAge.Elapsed.TotalSeconds<10)return;
            timer.Stop();
            if(mayOwnHardware && !recoveryConfirmed)MessageBox.Show(this,"尚未确认恢复系统自动。独立看护会在窗口退出后继续尝试；请勿结束所有后台进程，并保留日志。","恢复尚未确认",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            FinishClose();
        }
        void ReadRecoveryConfirmation(){
            if(run==null)return;
            var state=Files.Read<Frame>(Path.Combine(run,"state.json"));
            if(ExitRecoveryConfirmed(state,session))recoveryConfirmed=true;
            if(worker!=null && worker.HasExited){
                var guard=Files.Read<Frame>(Path.Combine(run,"guardian.json"));
                if(ExitRecoveryConfirmed(guard,session))recoveryConfirmed=true;
            }
        }
        void FinishClose(){
            if(finishClosing)return;finishClosing=true;timer.Stop();tray.Visible=false;tray.Dispose();if(worker!=null)worker.Dispose();BeginInvoke(new Action(Close));
        }
    }
}
