using System.IO.Ports;

namespace Espmon
{
    public abstract class Transport
    {
        long _lastReceiveTicks= -1;
        long _lastSendTicks= -1;

        protected Transport() { }
        protected abstract string GetName();
        public bool IsOpen { get; private set; }
        protected long LastSendTicks => _lastSendTicks;
        public TimeSpan TimeSinceLastSent
        {
            get
            {
                var val = LastSendTicks;
                if (val == -1) return TimeSpan.FromTicks(-1);
                return TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks-val);
            }
        }
        public TimeSpan TimeSinceLastReceived
        {
            get
            {
                var val = LastReceiveTicks;
                if (val == -1) return TimeSpan.FromTicks(-1);
                return TimeSpan.FromTicks(DateTimeOffset.UtcNow.Ticks-val);
            }
        }
        protected long LastReceiveTicks => _lastReceiveTicks;

        public int AvaialableLength => GetAvailableLength();
        protected virtual void OnClose()
        {

        }
        protected virtual void OnOpen()
        {

        }
        public virtual void DiscardAvailable()
        {
            var len = GetAvailableLength();
            if (len == 0) return;
            var ba = new byte[len];
            OnReceive(ba, 0, len);
        }
        public virtual int ReadByte(bool block=false)
        {
            if (block || GetAvailableLength() > 0)
            {
                var ba = new byte[1];
                OnReceive(ba,0,ba.Length);
                UpdateLastReceive();
                return ba[0];
            }
            return -1;
        }
        protected void UpdateLastReceive()
        {
            _lastReceiveTicks = DateTimeOffset.UtcNow.Ticks;
        }
        protected void UpdateLastSend()
        {
            _lastSendTicks = DateTimeOffset.UtcNow.Ticks;
        }
        protected abstract int GetAvailableLength();


        protected abstract void OnSend(byte[] data, int offset, int length);

        protected abstract void OnReceive(byte[] data, int offset, int length);

        public void Send(byte[] data, int offset, int length)
        {
            Open();
            OnSend(data, offset, length);
            UpdateLastSend();
        }
        
        public void Receive(byte[] data, int offset, int length)
        {
            Open();
            OnReceive(data, offset, length);
            UpdateLastReceive();
        }
        
        public void Open() 
        {
            if (!IsOpen)
            {
                OnOpen();
                IsOpen = true;
            }
        }
        public void Close()
        {
            if (IsOpen)
            {
                OnClose();
                IsOpen = false;
                _lastReceiveTicks = -1;
                _lastSendTicks = -1;
            }
            
        }
        
        public string Name => GetName();
    }
    public class SerialTransport : Transport
    {
        readonly SerialPort _port;
        public SerialTransport(SerialPort port, int readSize = 0, int writeSize = 0)
        {
            ArgumentNullException.ThrowIfNull(port, nameof(port));
            _port = port;
            if (readSize > 0)
            {
                _port.ReadBufferSize = readSize;
            }
            if (writeSize > 0)
            {
                _port.WriteBufferSize = writeSize;
            }
        }
        protected override string GetName()
        {
            return _port.PortName;
        }
        protected override void OnOpen()
        {
            _port.Open();
        }
        protected override void OnClose()
        {
            _port.Close();
        }
        protected override int GetAvailableLength()
        {
            if (_port.IsOpen)
            {
                return _port.BytesToRead;
            }
            return 0;
        }
        
        public override int ReadByte(bool block=false)
        {
            Open();
            if (!block && GetAvailableLength() == 0) return -1;
            var result = _port.ReadByte();
            if (block)
            {
                while(result<0)
                {
                    Task.Delay(50).Wait();
                    result = _port.ReadByte();
                }
            }
            UpdateLastReceive();
            return result;
        }
        public override void DiscardAvailable()
        {
            if(_port.IsOpen)
            {
                _port.DiscardInBuffer();
            }
        }
        protected override void OnSend(byte[] data, int offset, int length)
        {
            _port.Write(data, offset, length);
            _port.BaseStream.Flush();
        }
        protected override void OnReceive(byte[] data, int offset, int length)
        {
            var result = _port.Read(data, offset, length);
            while(result<length)
            {
                length -= result;
                offset+= result;
                result = _port.Read(data, offset, length);
            }
        }
    }
}
