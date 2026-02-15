using System;

namespace EL
{
	partial class EspLink : IDisposable
    {
		void Cleanup()
		{
			Device = null;
			_isSpiFlashAttached = false;
		}
		void IDisposable.Dispose()
		{
			Close();
			GC.SuppressFinalize(this);
		}
		/// <summary>
		/// Destroys this instance
		/// </summary>
		~EspLink()
		{
			Close();
		}
	}
}
