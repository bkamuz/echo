namespace echo.Abstractions.Platform;

/// <summary>Tiny radix-2 FFT for the dictation meter (N must be power of 2).</summary>
internal static class MiniFft
{
    public static void Forward(Span<float> re, Span<float> im)
    {
        var n = re.Length;
        if (n != im.Length || n < 2 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two.");
        }

        // Bit-reverse permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2.0 * Math.PI / len;
            var wlenRe = (float)Math.Cos(ang);
            var wlenIm = (float)Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                float wRe = 1f, wIm = 0f;
                var half = len >> 1;
                for (var j = 0; j < half; j++)
                {
                    var uRe = re[i + j];
                    var uIm = im[i + j];
                    var vRe = re[i + j + half] * wRe - im[i + j + half] * wIm;
                    var vIm = re[i + j + half] * wIm + im[i + j + half] * wRe;
                    re[i + j] = uRe + vRe;
                    im[i + j] = uIm + vIm;
                    re[i + j + half] = uRe - vRe;
                    im[i + j + half] = uIm - vIm;
                    var nextWRe = wRe * wlenRe - wIm * wlenIm;
                    wIm = wRe * wlenIm + wIm * wlenRe;
                    wRe = nextWRe;
                }
            }
        }
    }
}
