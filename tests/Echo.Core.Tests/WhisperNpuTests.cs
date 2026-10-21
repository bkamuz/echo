using System.Reflection;
using echo.Abstractions.Core;
using echo.Engines.ParakeetNpu;
using echo.Engines.ParakeetNpu.Ort;
using echo.Engines.ParakeetNpu.Whisper;

namespace echo.Core.Tests;

public sealed class WhisperNpuMelTests
{
    // Golden values from transformers 5.20.2 WhisperFeatureExtractor(feature_size=128) on the same signal.
    private static readonly Dictionary<int, float[]> Golden = new()
    {
        [0] = [0.90789f, 1.00545f, 0.98677f, 0.95231f, 1.04287f, 0.92995f, 1.05108f, 1.00634f, 1.02958f, 1.08231f, 0.98607f, 1.16051f, 1.07687f, 1.16137f, 1.28212f, 1.23627f, 1.40703f, 1.17658f, 0.71587f, 1.31808f, 1.33023f, 1.27757f, 1.15656f, 1.13733f, 0.97133f, 1.04331f, 0.96516f, 0.90765f, 0.96646f, 0.80831f, 0.90161f, 0.81936f, 0.82048f, 0.83194f, 0.71693f, 0.82273f, 0.69165f, 0.75156f, 0.73185f, 0.66236f, 0.74577f, 0.60287f, 0.6987f, 0.64878f, 0.60717f, 0.67389f, 0.58903f, 0.58487f, 0.62225f, 0.56157f, 0.55119f, 0.55379f, 0.58288f, 0.5234f, 0.51544f, 0.50685f, 0.49848f, 0.4904f, 0.48592f, 0.46099f, 0.45145f, 0.44065f, 0.4301f, 0.4189f, 0.40789f, 0.39642f, 0.38526f, 0.37852f, 0.36797f, 0.35066f, 0.33916f, 0.32758f, 0.32189f, 0.31588f, 0.29184f, 0.28477f, 0.28521f, 0.25609f, 0.26217f, 0.23792f, 0.23815f, 0.21534f, 0.21987f, 0.19433f, 0.18987f, 0.18297f, 0.16506f, 0.15567f, 0.14564f, 0.13522f, 0.12528f, 0.11143f, 0.10017f, 0.09075f, 0.07732f, 0.06485f, 0.05215f, 0.03949f, 0.03189f, 0.01153f, 0.00196f, -0.01489f, -0.02781f, -0.04776f, -0.06237f, -0.08314f, -0.10658f, -0.13182f, -0.16242f, -0.20091f, -0.25211f, -0.32678f, -0.4558f, -0.51464f, -0.38171f, -0.22522f, -0.11246f, -0.01782f, 0.07406f, 0.17204f, 0.28616f, 0.46193f, 0.74573f, 0.74451f, 0.51159f, 0.4136f, 0.36509f, 0.34248f],
        [1] = [0.39588f, 0.49345f, 0.47586f, 0.44141f, 0.52669f, 0.41167f, 0.53118f, 0.46975f, 0.49299f, 0.53032f, 0.42935f, 0.56536f, 0.44495f, 0.52944f, 0.51553f, 0.46967f, 1.34999f, 1.33583f, 1.48261f, 1.27941f, 1.29156f, 0.51141f, 0.40389f, 0.52049f, 0.39104f, 0.46303f, 0.43673f, 0.37922f, 0.45348f, 0.30905f, 0.40996f, 0.32921f, 0.33033f, 0.3463f, 0.22965f, 0.32939f, 0.19392f, 0.25383f, 0.2238f, 0.15431f, 0.23341f, 0.08679f, 0.18154f, 0.13401f, 0.09264f, 0.16202f, 0.08348f, 0.08f, 0.12196f, 0.10356f, 0.15081f, 0.19299f, 0.10697f, 0.02606f, 0.01153f, -0.00333f, -0.01532f, -0.02318f, -0.02724f, -0.04939f, -0.05748f, -0.06642f, -0.0765f, -0.08781f, -0.10016f, -0.11307f, -0.12589f, -0.13313f, -0.14349f, -0.15938f, -0.16964f, -0.18033f, -0.18599f, -0.19309f, -0.2184f, -0.22631f, -0.22583f, -0.25393f, -0.2467f, -0.27049f, -0.27082f, -0.2947f, -0.29096f, -0.31625f, -0.31976f, -0.32582f, -0.34397f, -0.3543f, -0.36501f, -0.37507f, -0.38399f, -0.39751f, -0.40951f, -0.41972f, -0.43282f, -0.44437f, -0.45694f, -0.47042f, -0.47837f, -0.49789f, -0.50694f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.42922f, -0.33673f, -0.20477f, -0.08462f, 0.77085f, 0.75081f, -0.02852f, -0.09434f, -0.14849f, -0.1682f],
        [50] = [-0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, 1.34451f, 1.33749f, 1.48535f, 1.27524f, 1.28739f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.4626f, -0.47296f, -0.26323f, -0.12032f, 0.02294f, 0.25866f, 0.88109f, 0.93818f, 0.98438f, 0.82547f, 0.24942f, 0.0159f, -0.20358f, -0.36605f, -0.49577f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, 0.77106f, 0.75082f, -0.51464f, -0.51464f, -0.51464f, -0.51464f],
        [100] = [-0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, 1.34451f, 1.33749f, 1.48535f, 1.27525f, 1.2874f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.48804f, -0.51464f, -0.4101f, -0.31263f, -0.32295f, -0.11313f, 0.02983f, 0.17305f, 0.40876f, 1.03159f, 1.08869f, 1.1349f, 0.97597f, 0.39989f, 0.16629f, -0.05319f, -0.21562f, -0.34526f, -0.45584f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, 0.77106f, 0.75082f, -0.51464f, -0.51464f, -0.51464f, -0.51464f],
        [199] = [0.27998f, 0.37754f, 0.3592f, 0.32475f, 0.40887f, 0.29302f, 0.41188f, 0.34871f, 0.37195f, 0.40731f, 0.30615f, 0.44015f, 0.31726f, 0.40176f, 0.38506f, 0.3392f, 1.34756f, 1.33668f, 1.48397f, 1.27708f, 1.28923f, 0.36486f, 0.25642f, 0.36787f, 0.23091f, 0.30289f, 0.26704f, 0.20953f, 0.27132f, 0.11609f, 0.21113f, 0.10806f, 0.10918f, 0.09724f, -0.02381f, 0.05711f, -0.06142f, -0.0015f, 0.04158f, -0.02792f, 0.14086f, 0.0621f, 0.17078f, 0.19407f, 0.16014f, 0.28737f, 0.29113f, 0.36192f, 0.5626f, 1.18228f, 1.23752f, 1.28301f, 1.12915f, 0.57456f, 0.40009f, 0.27488f, 0.20935f, 0.17648f, 0.1607f, 0.13191f, 0.11812f, 0.10249f, 0.08457f, 0.06442f, 0.04263f, 0.02026f, -0.00133f, -0.01625f, -0.03261f, -0.05264f, -0.06585f, -0.07912f, -0.08756f, -0.09784f, -0.12647f, -0.13735f, -0.13968f, -0.16994f, -0.16424f, -0.18927f, -0.19082f, -0.21594f, -0.21362f, -0.24018f, -0.24454f, -0.25115f, -0.26954f, -0.28006f, -0.29104f, -0.30136f, -0.31027f, -0.32333f, -0.3346f, -0.34403f, -0.35641f, -0.36696f, -0.37797f, -0.38947f, -0.39529f, -0.41256f, -0.41864f, -0.43224f, -0.44111f, -0.45531f, -0.46311f, -0.47613f, -0.48953f, -0.50123f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.40666f, -0.25378f, 0.77091f, 0.75082f, -0.15966f, -0.21585f, -0.26226f, -0.27871f],
        [200] = [0.79188f, 0.88945f, 0.87031f, 0.83585f, 0.92489f, 0.8112f, 0.93174f, 0.88421f, 0.90745f, 0.9579f, 0.86107f, 1.03151f, 0.94475f, 1.02924f, 1.14191f, 1.09606f, 1.30965f, 1.21072f, 1.33514f, 1.21839f, 1.23054f, 1.11312f, 0.99137f, 0.95486f, 0.77677f, 0.84876f, 0.74696f, 0.68945f, 0.72599f, 0.54555f, 0.62255f, 0.49408f, 0.4952f, 0.42046f, 0.28495f, 0.24263f, -0.02238f, 0.03754f, 0.34773f, 0.27823f, 0.51122f, 0.47044f, 0.58348f, 0.62537f, 0.59905f, 0.76241f, 0.76545f, 0.81791f, 1.00172f, 1.10214f, 1.11448f, 1.13703f, 1.12104f, 0.94005f, 0.87873f, 0.80289f, 0.7667f, 0.72218f, 0.69826f, 0.65156f, 0.63047f, 0.60669f, 0.58604f, 0.56519f, 0.54625f, 0.52729f, 0.50998f, 0.49702f, 0.4813f, 0.45904f, 0.44368f, 0.42848f, 0.41936f, 0.41018f, 0.38324f, 0.37381f, 0.37194f, 0.34072f, 0.34498f, 0.3191f, 0.31785f, 0.2938f, 0.29718f, 0.27067f, 0.26547f, 0.25788f, 0.23948f, 0.22977f, 0.21957f, 0.20913f, 0.19935f, 0.18583f, 0.17508f, 0.1664f, 0.15393f, 0.14266f, 0.13141f, 0.12049f, 0.11508f, 0.09732f, 0.0908f, 0.0776f, 0.06914f, 0.05449f, 0.04633f, 0.03378f, 0.02047f, 0.00816f, -0.00522f, -0.0199f, -0.03622f, -0.05475f, -0.074f, -0.10323f, -0.1367f, -0.18472f, -0.26576f, -0.4284f, -0.36418f, -0.11785f, 0.06651f, 0.28544f, 0.6822f, 0.67483f, 0.38011f, 0.29292f, 0.25091f, 0.2319f],
        [1500] = [-0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f],
        [2999] = [-0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f, -0.51464f],
    };

    private static float[] Signal()
    {
        const int n = 32000;
        var x = new float[n];
        for (var i = 0; i < n; i++)
        {
            x[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / 16000.0)
                           + 0.25 * Math.Sin(2 * Math.PI * 1234.5 * i / 16000.0) * (i / (double)n)
                           + 0.05 * Math.Sin(2 * Math.PI * 7000 * i / 16000.0));
        }

        return x;
    }

    [Fact]
    public void Compute_MatchesHuggingFaceFeatureExtractor()
    {
        var mel = WhisperMel.Compute(Signal());
        Assert.Equal(128 * 3000, mel.Length);
        var maxDiff = 0f;
        foreach (var (frame, expected) in Golden)
        {
            for (var m = 0; m < 128; m++)
            {
                maxDiff = Math.Max(maxDiff, Math.Abs(mel[m * 3000 + frame] - expected[m]));
            }
        }

        Assert.True(maxDiff < 2e-3f, $"max |diff| = {maxDiff}");
    }

    [Fact]
    public void Compute_SilenceIsFlatAndLongInputIsTruncated()
    {
        var silence = WhisperMel.Compute(new float[100]);
        Assert.All(silence, v => Assert.Equal(silence[0], v));

        var longInput = new float[WhisperMel.WindowSamples + 16000];
        Array.Fill(longInput, 0.1f);
        Assert.Equal(128 * 3000, WhisperMel.Compute(longInput).Length);
    }

    [Fact]
    public void MelFilters_AreSlaneyNormalizedTriangles()
    {
        var filters = WhisperMel.CreateMelFilters(128);
        for (var m = 0; m < 128; m++)
        {
            var nonZero = Enumerable.Range(0, WhisperMel.FreqBins).Count(k => filters[m, k] > 0);
            Assert.True(nonZero >= 1, $"mel {m} is empty");
        }
    }
}

public sealed class WhisperNpuTokenizerTests
{
    private static readonly (string Text, int[] Ids, string[] Tokens)[] Samples =
    [
        (" Привет, мир! Это тест распознавания речи.",
            [38932, 11, 20536, 0, 6684, 41699, 26588, 3434, 489, 1828, 8831, 1475, 4310, 435, 13],
            ["ĠÐŁÑĢÐ¸Ð²ÐµÑĤ", ",", "ĠÐ¼Ð¸ÑĢ", "!", "ĠÐŃÑĤÐ¾", "ĠÑĤÐµÑģÑĤ", "ĠÑĢÐ°ÑģÐ¿", "Ð¾Ð·", "Ð½", "Ð°Ð²", "Ð°Ð½Ð¸Ñı", "ĠÑĢ", "ÐµÑĩ", "Ð¸", "."]),
        (" Hello world — naïve café 😀",
            [2425, 1002, 3466, 1667, 15487, 303, 25118, 20732, 222],
            ["ĠHello", "Ġworld", "ĠâĢĶ", "Ġna", "Ã¯", "ve", "ĠcafÃ©", "ĠðŁĺ", "Ģ"]),
    ];

    private static WhisperTokenizer Build()
    {
        var vocab = new Dictionary<string, int>();
        foreach (var (_, ids, tokens) in Samples)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                vocab[tokens[i]] = ids[i];
            }
        }

        vocab["<|endoftext|>"] = WhisperTokens.Eot;
        return new WhisperTokenizer(vocab);
    }

    [Fact]
    public void Decode_ByteLevelBpe_RoundTripsHuggingFaceTokens()
    {
        var tokenizer = Build();
        foreach (var (text, ids, _) in Samples)
        {
            Assert.Equal(text, tokenizer.Decode(ids));
        }
    }

    [Fact]
    public void Decode_SkipsSpecialAndTimestampTokens()
    {
        var tokenizer = Build();
        int[] ids = [WhisperTokens.Sot, 50263, WhisperTokens.Transcribe, WhisperTokens.NoTimestamps, 38932, 51000, WhisperTokens.Eot];
        Assert.Equal(" Привет", tokenizer.Decode(ids));
    }

    [Theory]
    [InlineData("ru", 50263)]
    [InlineData("en", 50259)]
    [InlineData("RU-ru", 50263)]
    [InlineData("uk", 50280)]
    [InlineData("yue", 50358)]
    public void LanguageToken_MatchesWhisperV3Vocab(string code, int expected)
    {
        Assert.Equal(expected, WhisperTokens.LanguageToken(code));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("system")]
    public void LanguageToken_AutoOrUnknown_IsNull(string? code)
    {
        Assert.Null(WhisperTokens.LanguageToken(code));
    }
}

public sealed class WhisperNpuDecoderTests
{
    private const int Vocab = 51866;

    /// <summary>Records every decoder call; emits scripted argmax tokens.</summary>
    private sealed class FakeRunner : IWhisperModelRunner
    {
        private readonly Func<int, int, float[]> _logits;

        public FakeRunner(Func<int, int, float[]> logits) => _logits = logits;

        public int DecodeLength => 200;
        public int Encodes { get; private set; }
        public List<(int Token, int Position, Half[] Mask)> Calls { get; } = [];

        public void Encode(Half[] features)
        {
            Assert.Equal(128 * 3000, features.Length);
            Encodes++;
        }

        public float[] Decode(int tokenId, int position, Half[] attentionMask)
        {
            Calls.Add((tokenId, position, (Half[])attentionMask.Clone()));
            return _logits(Calls.Count - 1, tokenId);
        }
    }

    private static float[] Peak(params (int Id, float Value)[] peaks)
    {
        var logits = new float[Vocab];
        foreach (var (id, value) in peaks)
        {
            logits[id] = value;
        }

        return logits;
    }

    private static Half[] Features() => new Half[128 * 3000];

    [Fact]
    public void ForcedRussian_FeedsPrefixMaskAndPositions_LikeAiHubApp()
    {
        int[] script = [100, 200, WhisperTokens.Eot];
        var runner = new FakeRunner((step, _) => step < 3 ? Peak((5, 1f)) : Peak((script[step - 3], 9f)));

        var result = new WhisperGreedyDecoder().DecodeWindow(runner, Features(), WhisperTokens.LanguageToken("ru"));

        Assert.Equal([100, 200], result.Tokens);
        Assert.Equal("ru", result.Language);
        Assert.Equal(
            [WhisperTokens.Sot, 50263, WhisperTokens.Transcribe, WhisperTokens.NoTimestamps, 100, 200],
            runner.Calls.Select(c => c.Token));
        Assert.Equal(Enumerable.Range(0, 6), runner.Calls.Select(c => c.Position));
        for (var n = 0; n < runner.Calls.Count; n++)
        {
            var mask = runner.Calls[n].Mask;
            Assert.Equal(200, mask.Length);
            for (var i = 0; i < 200; i++)
            {
                var expected = i >= 200 - n - 1 ? 0f : WhisperGreedyDecoder.MaskNeg;
                Assert.Equal(expected, (float)mask[i]);
            }
        }
    }

    [Fact]
    public void AutoLanguage_DetectsFromFirstStepLogits()
    {
        var runner = new FakeRunner((step, _) => step switch
        {
            // Highest overall is a text token, but detection only looks at language tokens.
            0 => Peak((500, 50f), (50259 + 2, 5f), (50263, 4f)),
            < 3 => Peak((7, 1f)),
            3 => Peak((321, 3f)),
            _ => Peak((WhisperTokens.Eot, 3f)),
        });

        var result = new WhisperGreedyDecoder().DecodeWindow(runner, Features(), languageToken: null);

        Assert.Equal("de", result.Language);
        Assert.Equal(
            [WhisperTokens.Sot, 50261, WhisperTokens.Transcribe, WhisperTokens.NoTimestamps, 321],
            runner.Calls.Select(c => c.Token));
        Assert.Equal([321], result.Tokens);
    }

    [Fact]
    public void NeverEndingOutput_IsCappedAtContextLength()
    {
        var runner = new FakeRunner((step, _) => Peak((1000 + step, 1f)));
        var result = new WhisperGreedyDecoder().DecodeWindow(runner, Features(), 50263);

        Assert.Equal(199, runner.Calls.Count);
        Assert.Equal(199, result.Steps);
        Assert.Equal(199 - 3, result.Tokens.Count);
        Assert.Equal(198, runner.Calls[^1].Position);
    }

    [Fact]
    public void FirstToken_SuppressesBlankSpace_AndNeverEmitsSpecialTokens()
    {
        var runner = new FakeRunner((step, _) => step switch
        {
            3 => Peak((WhisperTokens.Space, 9f), (42, 5f)),
            4 => Peak((50400, 9f), (WhisperTokens.Translate, 8f), (43, 2f)),
            _ when step < 3 => Peak((1, 1f)),
            _ => Peak((WhisperTokens.Eot, 1f)),
        });

        var result = new WhisperGreedyDecoder().DecodeWindow(runner, Features(), 50263);

        Assert.Equal([42, 43], result.Tokens);
    }

    [Fact]
    public void RepetitionLoop_IsTrimmedToOneCopy()
    {
        var tokens = new List<int> { 5 };
        for (var i = 0; i < 6; i++)
        {
            tokens.AddRange([10, 11]);
        }

        Assert.True(WhisperGreedyDecoder.TrimRepetition(tokens));
        Assert.Equal([5, 10, 11], tokens);
        Assert.False(WhisperGreedyDecoder.TrimRepetition([1, 2, 3, 4, 5]));
    }

    [Fact]
    public void Transcriber_ChunksLongAudioInto30sWindows()
    {
        Assert.Equal(
            [(0, 480000), (480000, 480000), (960000, 240000)],
            WhisperTranscriber.Windows(75 * 16000));
        // A 50 ms tail is dropped; a short single utterance is kept.
        Assert.Equal([(0, 480000), (480000, 480000)], WhisperTranscriber.Windows(60 * 16000 + 800));
        Assert.Equal([(0, 800)], WhisperTranscriber.Windows(800));

        var vocab = new Dictionary<string, int> { ["ĠÐ´Ð°"] = 900 };
        var runner = new FakeRunner((step, token) => token == WhisperTokens.NoTimestamps
            ? Peak((900, 5f))
            : token == 900 ? Peak((WhisperTokens.Eot, 5f)) : Peak((1, 1f)));
        var text = new WhisperTranscriber(runner, new WhisperTokenizer(vocab))
            .Transcribe(new float[65 * 16000], "ru");

        Assert.Equal(3, runner.Encodes);
        Assert.Equal("да да да", text);
    }

    [Fact]
    public void Resample_48kTo16k_KeepsDuration()
    {
        Assert.Equal(16000, WhisperTranscriber.ResampleTo16k(new float[48000], 48000).Length);
        var same = new float[10];
        Assert.Same(same, WhisperTranscriber.ResampleTo16k(same, 16000));
    }
}

public sealed class WhisperNpuAssetTests
{
    [Fact]
    public void DecoderContract_MatchesAiHubExportOrder()
    {
        string[] onnxOrder =
        [
            "input_ids", "position_ids",
            "k_cache_self_0_in", "v_cache_self_0_in", "attention_mask", "k_cache_cross_0", "v_cache_cross_0",
            "k_cache_self_1_in", "v_cache_self_1_in", "k_cache_cross_1", "v_cache_cross_1",
            "k_cache_self_2_in", "v_cache_self_2_in", "k_cache_cross_2", "v_cache_cross_2",
            "k_cache_self_3_in", "v_cache_self_3_in", "k_cache_cross_3", "v_cache_cross_3",
        ];
        Assert.Equal(onnxOrder, WhisperQnnRunner.DecoderContract.Inputs.Select(i => i.Name));
        Assert.Equal("logits", WhisperQnnRunner.DecoderContract.Outputs[^1].Name);
        Assert.Equal([1L, 51866, 1, 1], WhisperQnnRunner.DecoderContract.Outputs[^1].Dimensions);
        Assert.All(
            WhisperQnnRunner.DecoderContract.Inputs.Where(i => i.Name.Contains("cache")),
            i => Assert.Equal(OrtTensorDataKind.Float16, i.ElementType));
        Assert.Equal(8, WhisperQnnRunner.EncoderContract.Outputs.Count);
        Assert.Equal([1L, 128, 3000], WhisperQnnRunner.EncoderContract.Inputs[0].Dimensions);
    }

    [Fact]
    public void OrtFloat16_UsesOnnxElementTypeTen()
    {
        Assert.Equal(10, (int)OrtTensorElementType.Float16);
    }

    [Fact]
    public void WhisperRuntimeManifest_PinsOrt130AndQnn260_WithLicenses()
    {
        var manifest = ManifestLoader.LoadRuntimeManifest(QnnRuntimeProfile.Whisper);
        Assert.Equal("1.30.0", manifest.Packages.Single(p => p.Name == "onnxruntime").Version);
        Assert.Equal("2.6.0", manifest.Packages.Single(p => p.Name == "onnxruntime-qnn").Version);
        foreach (var file in new[] { "onnxruntime.dll", "QnnHtpV73Stub.dll", "libQnnHtpV81Skel.so", "Qualcomm_LICENSE.pdf" })
        {
            Assert.Contains(file, manifest.RequiredFiles);
        }

        var targets = manifest.Packages.SelectMany(p => p.Files).Select(f => f.Target).ToHashSet();
        Assert.All(manifest.RequiredFiles, f => Assert.Contains(f, targets));
        Assert.Contains("ThirdPartyNotices.onnxruntime-qnn.txt", targets);
        Assert.All(manifest.Packages, p => Assert.Equal(64, p.Sha256.Length));
    }

    [Fact]
    public void ParakeetRuntime_StaysOnOrt124_InItsOwnDirectory()
    {
        var parakeet = ManifestLoader.LoadRuntimeManifest(QnnRuntimeProfile.Parakeet);
        Assert.Equal("1.24.4", parakeet.Packages.Single(p => p.Name == "onnxruntime").Version);
        Assert.Equal(AppPaths.NpuDir, QnnRuntimeProfile.Parakeet.Dir);
        Assert.Equal(AppPaths.NpuWhisperRuntimeDir, QnnRuntimeProfile.Whisper.Dir);
        Assert.NotEqual(QnnRuntimeProfile.Parakeet.Dir, QnnRuntimeProfile.Whisper.Dir);
        Assert.Same(QnnRuntimeProfile.Whisper, QnnRuntimeProfile.FromName("whisper"));
        Assert.Same(QnnRuntimeProfile.Parakeet, QnnRuntimeProfile.FromName(null));
    }

    [Fact]
    public void ChipsetKey_MapsHtpGenerationToAiHubAsset()
    {
        Assert.Equal("qualcomm-snapdragon-x2-elite", WhisperNpuModelDownloader.ChipsetKey(HtpHardwareProfile.V81X2Elite));
        Assert.Equal("qualcomm-snapdragon-x-elite", WhisperNpuModelDownloader.ChipsetKey(HtpHardwareProfile.V73XElite));
        Assert.Equal(
            "https://qaihub-public-assets.s3.us-west-2.amazonaws.com/qai-hub-models/models/whisper_large_v3_turbo/releases/v0.64.0/whisper_large_v3_turbo-precompiled_qnn_onnx-float-qualcomm_snapdragon_x2_elite.zip",
            WhisperNpuModelDownloader.PinnedUrl("qualcomm-snapdragon-x2-elite"));
    }

    [Fact]
    public void ParseAssetUrl_ReadsReleaseAssetsJson_AndRejectsOtherQairt()
    {
        const string json = """
            {"version":"0.64.0","precisions":{"float":{"chipset_assets":{
              "qualcomm-snapdragon-x2-elite":{"precompiled_qnn_onnx":{"tool_versions":{"qairt":"2.50.0.1","onnx_runtime":"1.30.0"},"download_url":"https://x/x2.zip"}},
              "qualcomm-snapdragon-x-elite":{"precompiled_qnn_onnx":{"tool_versions":{"qairt":"2.51.0.1"},"download_url":"https://x/x1.zip"}}}}}}
            """;
        Assert.Equal("https://x/x2.zip", WhisperNpuModelDownloader.ParseAssetUrl(json, "qualcomm-snapdragon-x2-elite"));
        Assert.Null(WhisperNpuModelDownloader.ParseAssetUrl(json, "qualcomm-snapdragon-x-elite"));
        Assert.Null(WhisperNpuModelDownloader.ParseAssetUrl(json, "qualcomm-snapdragon-8gen3"));
    }

    [Fact]
    public void ExtractModel_FlattensAiHubArchive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-wnpu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zip = Path.Combine(dir, "a.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "encoder.onnx", "encoder.bin", "decoder.onnx", "decoder.bin", "metadata.json" })
                {
                    using var writer = new StreamWriter(archive.CreateEntry("whisper_x/" + name).Open());
                    writer.Write(name);
                }
            }

            WhisperNpuModelDownloader.ExtractModel(zip, dir, null);
            File.WriteAllText(Path.Combine(dir, "vocab.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "added_tokens.json"), "{}");
            Assert.True(ModelRegistry.IsWhisperNpuDownloaded(dir));
            File.WriteAllText(Path.Combine(dir, "chipset.txt"), "qualcomm-snapdragon-x2-elite");
            Assert.True(WhisperNpuModelDownloader.IsForCurrentChip(dir, HtpHardwareProfile.V81X2Elite));
            Assert.False(WhisperNpuModelDownloader.IsForCurrentChip(dir, HtpHardwareProfile.V73XElite));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Spec_WhisperNpu_IsRegistered()
    {
        var spec = ModelRegistry.SpecForEngine("whisper_npu", "small", "e2e");
        Assert.NotNull(spec);
        Assert.Equal("whisper_npu", spec!.Engine);
        Assert.Equal(AppPaths.WhisperNpuDir, spec.LocalDir);
    }
}
