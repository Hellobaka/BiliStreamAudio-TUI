using BiliStreamAudio.Tui.Core;
using System.Numerics;

namespace BiliStreamAudio.Tui.Infrastructure;

/// <summary>
/// 从固定格式的 PCM 音频计算显示用频谱。采样写入和 FFT 计算分属不同线程，
/// 以免阻塞 LibVLC 的实时音频回调。
/// </summary>
internal sealed class AudioSpectrumAnalyzer : IAudioSpectrumSource, IDisposable
{
    private const int SampleRate = 48_000;
    private const int WindowSize = 2_048;
    private const int OutputBandCount = 64;
    private const int UpdateIntervalMilliseconds = 80;
    private const double MinimumFrequency = 40;
    private const double MaximumFrequency = 16_000;

    private readonly object _sync = new();
    private readonly float[] _samples = new float[WindowSize];
    private readonly float[] _previousMagnitudes = new float[OutputBandCount];
    private readonly Complex[] _fftBuffer = new Complex[WindowSize];
    private readonly System.Threading.Timer _timer;
    private int _writeIndex;
    private int _sampleCount;
    private int _calculating;
    private bool _started;
    private bool _enabled;

    public AudioSpectrumAnalyzer()
    {
        _timer = new System.Threading.Timer(OnTimerTick);
    }

    public event EventHandler<SpectrumFrame>? SpectrumChanged;

    public SpectrumFrame? CurrentSpectrum { get; private set; }

    public void SetSpectrumEnabled(bool enabled) => SetEnabled(enabled);

    public void SetEnabled(bool enabled)
    {
        bool shouldRun;
        lock (_sync)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            if (!enabled)
            {
                _sampleCount = 0;
                CurrentSpectrum = null;
            }

            shouldRun = enabled && _started;
        }

        _timer.Change(shouldRun ? 0 : Timeout.Infinite, shouldRun ? UpdateIntervalMilliseconds : Timeout.Infinite);
    }

    public void Start()
    {
        lock (_sync)
        {
            Array.Clear(_samples);
            Array.Clear(_previousMagnitudes);
            _writeIndex = 0;
            _sampleCount = 0;
            CurrentSpectrum = null;
            _started = true;
        }

        if (_enabled)
        {
            _timer.Change(0, UpdateIntervalMilliseconds);
        }
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        lock (_sync)
        {
            _started = false;
            _sampleCount = 0;
            CurrentSpectrum = null;
        }

        SpectrumChanged?.Invoke(this, new SpectrumFrame([]));
    }

    /// <summary>接收 48kHz、双声道、16 位 little-endian PCM。</summary>
    public void PushPcm16Stereo(byte[] data, int length)
    {
        lock (_sync)
        {
            if (!_started || !_enabled)
            {
                return;
            }

            var frameLength = length - length % 4;
            for (var offset = 0; offset < frameLength; offset += 4)
            {
                var left = (short)(data[offset] | data[offset + 1] << 8);
                var right = (short)(data[offset + 2] | data[offset + 3] << 8);
                _samples[_writeIndex] = (left + right) / (2f * short.MaxValue);
                _writeIndex = (_writeIndex + 1) % WindowSize;
                _sampleCount = Math.Min(_sampleCount + 1, WindowSize);
            }
        }
    }

    private void OnTimerTick(object? state)
    {
        if (Interlocked.Exchange(ref _calculating, 1) != 0)
        {
            return;
        }

        try
        {
            CalculateSpectrum();
        }
        finally
        {
            Volatile.Write(ref _calculating, 0);
        }
    }

    private void CalculateSpectrum()
    {
        var previousMagnitudes = new float[OutputBandCount];
        lock (_sync)
        {
            if (!_started || !_enabled || _sampleCount < WindowSize)
            {
                return;
            }

            for (var index = 0; index < WindowSize; index++)
            {
                var sample = _samples[(_writeIndex + index) % WindowSize];
                var hann = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (WindowSize - 1));
                _fftBuffer[index] = new Complex(sample * hann, 0);
            }

            Array.Copy(_previousMagnitudes, previousMagnitudes, OutputBandCount);
        }

        Transform(_fftBuffer);
        var magnitudes = new float[OutputBandCount];
        for (var band = 0; band < OutputBandCount; band++)
        {
            var lowerFrequency = BandFrequency(band);
            var upperFrequency = BandFrequency(band + 1);
            var firstBin = Math.Max(1, (int)Math.Floor(lowerFrequency * WindowSize / SampleRate));
            var lastBin = Math.Min(WindowSize / 2 - 1, (int)Math.Ceiling(upperFrequency * WindowSize / SampleRate));
            var peak = 0d;
            for (var bin = firstBin; bin <= lastBin; bin++)
            {
                peak = Math.Max(peak, _fftBuffer[bin].Magnitude * 2 / WindowSize);
            }

            var decibels = 20 * Math.Log10(Math.Max(peak, 0.000_001));
            var normalized = (float)Math.Clamp((decibels + 72) / 72, 0, 1);
            magnitudes[band] = Math.Max(normalized, previousMagnitudes[band] * 0.72f);
        }

        var spectrum = new SpectrumFrame(magnitudes);
        lock (_sync)
        {
            if (!_started || !_enabled)
            {
                return;
            }

            Array.Copy(magnitudes, _previousMagnitudes, OutputBandCount);
            CurrentSpectrum = spectrum;
        }

        SpectrumChanged?.Invoke(this, spectrum);
    }

    private static double BandFrequency(int band) => MinimumFrequency * Math.Pow(
        MaximumFrequency / MinimumFrequency,
        band / (double)OutputBandCount);

    private static void Transform(Complex[] values)
    {
        for (int index = 1, bit = 0; index < values.Length; index++)
        {
            for (var mask = values.Length >> 1; (bit & mask) != 0; mask >>= 1)
            {
                bit &= ~mask;
            }

            bit |= values.Length >> 1;
            if (index < bit)
            {
                (values[index], values[bit]) = (values[bit], values[index]);
            }
        }

        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var start = 0; start < values.Length; start += length)
            {
                var twiddle = Complex.One;
                var half = length / 2;
                for (var offset = 0; offset < half; offset++)
                {
                    var even = values[start + offset];
                    var odd = values[start + offset + half] * twiddle;
                    values[start + offset] = even + odd;
                    values[start + offset + half] = even - odd;
                    twiddle *= step;
                }
            }
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
