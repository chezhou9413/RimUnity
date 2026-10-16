using System;
using System.Collections;
using System.Diagnostics;

namespace RimUnity.Editor
{
    //Main-thread owned. Disposal closes the nested iterator and any open file streams.
    internal sealed class ScanStepper : IDisposable
    {
        private IEnumerator work;
        internal ScanStepper(IEnumerator work) { this.work = work ?? throw new ArgumentNullException(nameof(work)); }
        internal bool Advance(int maxSteps = 24, double budgetMilliseconds = 8)
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < maxSteps && work != null; i++)
            {
                if (!work.MoveNext()) { Dispose(); return true; }
                if (clock.Elapsed.TotalMilliseconds >= budgetMilliseconds) break;
            }
            return work == null;
        }
        public void Dispose()
        {
            var previous = work; work = null;
            (previous as IDisposable)?.Dispose();
        }
    }
}
