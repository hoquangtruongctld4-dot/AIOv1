using System;

namespace subphimv1.SoundTouch
{
    public sealed class BPMDetect : IDisposable
    {
        #region Private Members

        private readonly object SyncRoot = new object();
        private bool IsDisposed = false;
        private IntPtr handle;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="BPMDetect"/> class.
        /// </summary>
        public BPMDetect(int numChannels, int sampleRate)
        {
            handle = NativeMethods.BpmCreateInstance(numChannels, sampleRate);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="BPMDetect"/> class.
        /// </summary>
        ~BPMDetect()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the analysed BPM rate.
        /// </summary>
        public float Bpm
        {
            get { lock (SyncRoot) { return NativeMethods.BpmGet(handle); } }
        }

        #endregion

        #region Sample Stream Methods

        /// <summary>
        /// Feed 'numSamples' sample into the BPM detector
        /// </summary>
        /// <param name="samples">Sample buffer to input</param>
        /// <param name="numSamples">Number of sample frames in buffer. Notice
        /// that in case of multi-channel sound a single sample frame contains 
        /// data for all channels</param>
        public void PutSamples(float[] samples, uint numSamples)
        {
            lock (SyncRoot) { NativeMethods.BpmPutSamples(handle, samples, numSamples); }
        }

        /// <summary>
        /// int16 version of putSamples(): This accept int16 (short) sample data
        /// and internally converts it to float format before processing
        /// </summary>
        /// <param name="samples">Sample input buffer.</param>
        /// <param name="numSamples">Number of sample frames in buffer. Notice
        /// that in case of multi-channel sound a single 
        /// sample frame contains data for all channels.</param>
        public void PutSamplesI16(short[] samples, uint numSamples)
        {
            lock (SyncRoot) { NativeMethods.BpmPutSamples_i16(handle, samples, numSamples); }
        }

        #endregion

        #region IDisposable Support

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        private void Dispose(bool alsoManaged)
        {
            if (!IsDisposed)
            {
                if (alsoManaged)
                {
                    // NOTE: Placeholder, dispose managed state (managed objects).
                    // At this point, nothing managed to dispose
                }

                NativeMethods.BpmDestroyInstance(handle);
                handle = IntPtr.Zero;

                IsDisposed = true;
            }
        }

        #endregion
    }
}
