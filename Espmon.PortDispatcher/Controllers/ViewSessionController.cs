using System.Diagnostics;

namespace Espmon
{
    public sealed class ViewSessionController : SessionController
    {
        public ViewSessionController(PortController parent) : base(parent, "<local>", "00000")
        {
            Device = new DeviceController(parent, "<view>");
        }
        protected override void OnConnect()
        {
            Status = SessionStatus.Busy;
        }

        protected override void OnDisconnect()
        {
            Status = SessionStatus.Closed;
        }

        protected override Task OnFlashAsync(FirmwareEntry firmwareEntry, IFlashProgress? progress, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
        protected override void OnScreenIndexChanged()
        {
            var clearArgs = EventArgs.Empty;
            OnScreenCleared(clearArgs);
            var changeArgs = new ScreenChangedEventArgs(ScreenIndex);
            OnScreenChanged(changeArgs);
        }
        protected override void OnRefresh()
        {

            if ( ScreenIndex > -1 && Device!=null && Device.Screens.Count>0)
            {
                var scr = Screen;
                if (scr != null)
                {
                    var args = new ScreenDataEventArgs(
                        ScreenIndex,
                        scr.Top.Value1.Value,
                        scr.Top.Value1.Scaled,
                        scr.Top.Value2.Value,
                        scr.Top.Value2.Scaled,
                        scr.Bottom.Value1.Value,
                        scr.Bottom.Value1.Scaled,
                        scr.Bottom.Value2.Value,
                        scr.Bottom.Value2.Scaled
                    );
                    OnScreenData(args);

                }
            } 
        }

        protected override Task OnResetAsync(IFlashProgress? progress, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
