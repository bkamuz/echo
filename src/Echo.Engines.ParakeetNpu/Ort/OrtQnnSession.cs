using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Ort;

internal enum OrtTensorDataKind
{
    Float32,
    Int32,
}

internal sealed class OrtTensorSpec
{
    public required string Name { get; init; }
    public required OrtTensorDataKind ElementType { get; init; }
    public required long[] Dimensions { get; init; }
}

internal sealed class OrtSessionContract
{
    public required IReadOnlyList<OrtTensorSpec> Inputs { get; init; }
    public required IReadOnlyList<OrtTensorSpec> Outputs { get; init; }

    public static OrtSessionContract ParakeetEncoder { get; } = new()
    {
        Inputs =
        [
            new OrtTensorSpec
            {
                Name = "audio_signal",
                ElementType = OrtTensorDataKind.Float32,
                Dimensions = [1, 128, 801],
            },
            new OrtTensorSpec
            {
                Name = "length",
                ElementType = OrtTensorDataKind.Int32,
                Dimensions = [1],
            },
        ],
        Outputs =
        [
            new OrtTensorSpec
            {
                Name = "output_0",
                ElementType = OrtTensorDataKind.Float32,
                Dimensions = [1, 1024, -1],
            },
            new OrtTensorSpec
            {
                Name = "output_1",
                ElementType = OrtTensorDataKind.Int32,
                Dimensions = [1],
            },
        ],
    };
}

internal readonly struct OrtTensorInput
{
    public OrtTensorInput(string name, long[] dimensions, float[] data)
    {
        Name = name;
        Dimensions = dimensions;
        Kind = OrtTensorDataKind.Float32;
        FloatData = data;
        IntData = null;
    }

    public OrtTensorInput(string name, long[] dimensions, int[] data)
    {
        Name = name;
        Dimensions = dimensions;
        Kind = OrtTensorDataKind.Int32;
        FloatData = null;
        IntData = data;
    }

    public string Name { get; }
    public long[] Dimensions { get; }
    public OrtTensorDataKind Kind { get; }
    public float[]? FloatData { get; }
    public int[]? IntData { get; }
}

internal sealed class OrtTensorOutput
{
    public required string Name { get; init; }
    public required long[] Dimensions { get; init; }
    public required OrtTensorDataKind ElementType { get; init; }
    public required float[] FloatData { get; init; }
    public required int[] IntData { get; init; }

    public (long[] Dimensions, float[] Values) IntoFloat()
    {
        if (ElementType != OrtTensorDataKind.Float32)
        {
            throw new InvalidOperationException($"Output {Name} is not float32.");
        }

        return (Dimensions, FloatData);
    }

    public (long[] Dimensions, int[] Values) IntoInt32()
    {
        if (ElementType != OrtTensorDataKind.Int32)
        {
            throw new InvalidOperationException($"Output {Name} is not int32.");
        }

        return (Dimensions, IntData);
    }
}

/// <summary>
/// Direct ORT C-API session for the QNN HTP Parakeet encoder wrapper ONNX.
/// </summary>
internal sealed unsafe class OrtQnnSession : IDisposable
{
    private readonly OrtApiTable _api;
    private readonly OrtSessionContract _contract;
    private readonly QnnProviderLease _providerLease;
    private readonly List<GCHandle> _pinnedInputs = [];
    private nint _session;
    private nint _memoryInfo;
    private readonly byte*[] _inputNamePtrs;
    private readonly byte*[] _outputNamePtrs;

    private OrtQnnSession(
        OrtApiTable api,
        nint session,
        nint memoryInfo,
        OrtSessionContract contract,
        QnnProviderLease providerLease,
        byte*[] inputNamePtrs,
        byte*[] outputNamePtrs)
    {
        _api = api;
        _session = session;
        _memoryInfo = memoryInfo;
        _contract = contract;
        _providerLease = providerLease;
        _inputNamePtrs = inputNamePtrs;
        _outputNamePtrs = outputNamePtrs;
    }

    public static OrtQnnSession Load(
        string modelPath,
        IReadOnlyList<nint> qnnNpuDevices,
        OrtSessionContract contract,
        QnnProviderLease providerLease,
        ILogger? logger = null)
    {
        if (qnnNpuDevices.Count == 0)
        {
            throw new InvalidOperationException("No QNN NPU device was provided.");
        }

        OrtQnnBootstrap.EnsureComApartment();
        var api = OrtApiNative.Api;
        var env = OrtQnnBootstrap.EnvHandle;

        nint sessionOptions = 0;
        nint session = 0;
        nint memoryInfo = 0;
        byte*[]? inputNamePtrs = null;
        byte*[]? outputNamePtrs = null;

        try
        {
            OrtApiNative.Check(api.CreateSessionOptions(&sessionOptions), "CreateSessionOptions");
            OrtApiNative.Check(
                api.SetSessionGraphOptimizationLevel(sessionOptions, OrtGraphOptimizationLevel.EnableAll),
                "SetSessionGraphOptimizationLevel");

            var performanceKey = Marshal.StringToCoTaskMemUTF8("htp_performance_mode");
            var performanceValue = Marshal.StringToCoTaskMemUTF8("burst");
            nint* keys = stackalloc nint[1];
            nint* values = stackalloc nint[1];
            keys[0] = performanceKey;
            values[0] = performanceValue;
            fixed (nint* devicePtr = qnnNpuDevices.ToArray())
            {
                OrtApiNative.Check(
                    api.SessionOptionsAppendExecutionProviderV2(
                        sessionOptions,
                        env,
                        devicePtr,
                        (nuint)qnnNpuDevices.Count,
                        keys,
                        values,
                        1),
                    "SessionOptionsAppendExecutionProvider_V2");
            }

            Marshal.FreeCoTaskMem(performanceKey);
            Marshal.FreeCoTaskMem(performanceValue);

            var ortPath = OrtApiNative.ToOrtPath(modelPath);
            fixed (char* pathPtr = ortPath)
            {
                logger?.LogInformation("Creating QNN HTP session for {ModelPath}", modelPath);
                OrtApiNative.Check(
                    api.CreateSession(env, (nint)pathPtr, sessionOptions, &session),
                    "CreateSession");
            }

            ValidateSessionContract(api, session, contract);

            OrtApiNative.Check(
                api.CreateCpuMemoryInfo(OrtAllocatorType.ArenaAllocator, OrtMemType.Default, &memoryInfo),
                "CreateCpuMemoryInfo");

            inputNamePtrs = new byte*[contract.Inputs.Count];
            for (var i = 0; i < contract.Inputs.Count; i++)
            {
                inputNamePtrs[i] = (byte*)Marshal.StringToCoTaskMemUTF8(contract.Inputs[i].Name);
            }

            outputNamePtrs = new byte*[contract.Outputs.Count];
            for (var i = 0; i < contract.Outputs.Count; i++)
            {
                outputNamePtrs[i] = (byte*)Marshal.StringToCoTaskMemUTF8(contract.Outputs[i].Name);
            }

            return new OrtQnnSession(
                api,
                session,
                memoryInfo,
                contract,
                providerLease,
                inputNamePtrs,
                outputNamePtrs);
        }
        catch
        {
            if (inputNamePtrs is not null)
            {
                FreeNamePtrs(inputNamePtrs);
            }

            if (outputNamePtrs is not null)
            {
                FreeNamePtrs(outputNamePtrs);
            }

            if (memoryInfo != 0)
            {
                api.ReleaseMemoryInfo(memoryInfo);
            }

            if (session != 0)
            {
                api.ReleaseSession(session);
            }

            if (sessionOptions != 0)
            {
                api.ReleaseSessionOptions(sessionOptions);
            }

            providerLease.Dispose();
            throw;
        }
        finally
        {
            if (sessionOptions != 0)
            {
                api.ReleaseSessionOptions(sessionOptions);
            }
        }
    }

    public IReadOnlyList<OrtTensorOutput> Run(IReadOnlyList<OrtTensorInput> inputs)
    {
        OrtQnnBootstrap.EnsureComApartment();
        ValidateInputs(_contract.Inputs, inputs);
        ReleasePinnedInputs();

        var inputValues = new nint[inputs.Count];
        try
        {
            for (var i = 0; i < inputs.Count; i++)
            {
                inputValues[i] = CreateInputValue(inputs[i]);
            }

            var outputValues = new nint[_contract.Outputs.Count];
            fixed (byte** inputNames = _inputNamePtrs)
            fixed (nint* inputValuePtrs = inputValues)
            fixed (byte** outputNames = _outputNamePtrs)
            fixed (nint* outputValuePtrs = outputValues)
            {
                OrtApiNative.Check(
                    _api.Run(
                        _session,
                        0,
                        (nint*)inputNames,
                        inputValuePtrs,
                        (nuint)inputs.Count,
                        (nint*)outputNames,
                        (nuint)_contract.Outputs.Count,
                        outputValuePtrs),
                    "Run");
            }

            var outputs = new List<OrtTensorOutput>(_contract.Outputs.Count);
            for (var i = 0; i < _contract.Outputs.Count; i++)
            {
                try
                {
                    outputs.Add(ExtractOutput(outputValues[i], _contract.Outputs[i]));
                }
                finally
                {
                    if (outputValues[i] != 0)
                    {
                        _api.ReleaseValue(outputValues[i]);
                    }
                }
            }

            return outputs;
        }
        finally
        {
            for (var i = 0; i < inputValues.Length; i++)
            {
                if (inputValues[i] != 0)
                {
                    _api.ReleaseValue(inputValues[i]);
                }
            }
        }
    }

    public void Dispose()
    {
        ReleasePinnedInputs();
        FreeNamePtrs(_inputNamePtrs);
        FreeNamePtrs(_outputNamePtrs);

        if (_memoryInfo != 0)
        {
            _api.ReleaseMemoryInfo(_memoryInfo);
            _memoryInfo = 0;
        }

        if (_session != 0)
        {
            _api.ReleaseSession(_session);
            _session = 0;
        }

        _providerLease.Dispose();
    }

    private nint CreateInputValue(OrtTensorInput input)
    {
        var spec = _contract.Inputs.First(s => s.Name == input.Name);
        var elementCount = ElementCount(input.Dimensions);
        nint dataPtr;
        nuint byteSize;
        OrtTensorElementType elementType;

        switch (input.Kind)
        {
            case OrtTensorDataKind.Float32:
                if (input.FloatData is null || input.FloatData.Length != elementCount)
                {
                    throw new InvalidOperationException(
                        $"Input {input.Name} expects {elementCount} float elements.");
                }

                var floatHandle = GCHandle.Alloc(input.FloatData, GCHandleType.Pinned);
                _pinnedInputs.Add(floatHandle);
                dataPtr = floatHandle.AddrOfPinnedObject();
                byteSize = (nuint)(elementCount * sizeof(float));
                elementType = OrtTensorElementType.Float;
                break;
            case OrtTensorDataKind.Int32:
                if (input.IntData is null || input.IntData.Length != elementCount)
                {
                    throw new InvalidOperationException(
                        $"Input {input.Name} expects {elementCount} int32 elements.");
                }

                var intHandle = GCHandle.Alloc(input.IntData, GCHandleType.Pinned);
                _pinnedInputs.Add(intHandle);
                dataPtr = intHandle.AddrOfPinnedObject();
                byteSize = (nuint)(elementCount * sizeof(int));
                elementType = OrtTensorElementType.Int32;
                break;
            default:
                throw new InvalidOperationException($"Unsupported input type for {input.Name}.");
        }

        if (spec.ElementType != input.Kind)
        {
            throw new InvalidOperationException($"Input {input.Name} type mismatch.");
        }

        if (!ShapeMatches(spec.Dimensions, input.Dimensions))
        {
            throw new InvalidOperationException(
                $"Input {input.Name} shape mismatch: expected [{string.Join(',', spec.Dimensions)}], " +
                $"got [{string.Join(',', input.Dimensions)}].");
        }

        nint value = 0;
        fixed (long* dims = input.Dimensions)
        {
            OrtApiNative.Check(
                _api.CreateTensorWithDataAsOrtValue(
                    _memoryInfo,
                    dataPtr,
                    byteSize,
                    (nint)dims,
                    (nuint)input.Dimensions.Length,
                    elementType,
                    &value),
                $"CreateTensorWithDataAsOrtValue({input.Name})");
        }

        return value;
    }

    private OrtTensorOutput ExtractOutput(nint value, OrtTensorSpec spec)
    {
        nint shapeInfo = 0;
        try
        {
            OrtApiNative.Check(_api.GetTensorTypeAndShape(value, &shapeInfo), "GetTensorTypeAndShape");
            var (elementType, dimensions) = ReadTensorMetadata(shapeInfo);
            if (!MapsTo(spec.ElementType, elementType))
            {
                throw new InvalidOperationException(
                    $"Output {spec.Name} type mismatch: expected {spec.ElementType}, got {elementType}.");
            }

            if (!ShapeMatches(spec.Dimensions, dimensions))
            {
                throw new InvalidOperationException(
                    $"Output {spec.Name} shape mismatch: expected [{string.Join(',', spec.Dimensions)}], " +
                    $"got [{string.Join(',', dimensions)}].");
            }

            var elements = ElementCount(dimensions);
            nint dataPtr = 0;
            if (elements > 0)
            {
                OrtApiNative.Check(_api.GetTensorMutableData(value, &dataPtr), "GetTensorMutableData");
                if (dataPtr == 0)
                {
                    throw new InvalidOperationException($"Output {spec.Name} returned a null data pointer.");
                }
            }

            return elementType switch
            {
                OrtTensorElementType.Float => new OrtTensorOutput
                {
                    Name = spec.Name,
                    Dimensions = dimensions,
                    ElementType = OrtTensorDataKind.Float32,
                    FloatData = elements == 0
                        ? []
                        : new ReadOnlySpan<float>((void*)dataPtr, elements).ToArray(),
                    IntData = [],
                },
                OrtTensorElementType.Int32 => new OrtTensorOutput
                {
                    Name = spec.Name,
                    Dimensions = dimensions,
                    ElementType = OrtTensorDataKind.Int32,
                    FloatData = [],
                    IntData = elements == 0
                        ? []
                        : new ReadOnlySpan<int>((void*)dataPtr, elements).ToArray(),
                },
                _ => throw new InvalidOperationException($"Unsupported output element type {elementType}."),
            };
        }
        finally
        {
            if (shapeInfo != 0)
            {
                _api.ReleaseTensorTypeAndShapeInfo(shapeInfo);
            }
        }
    }

    private (OrtTensorElementType ElementType, long[] Dimensions) ReadTensorMetadata(nint shapeInfo)
    {
        OrtTensorElementType elementType = OrtTensorElementType.Undefined;
        OrtApiNative.Check(_api.GetTensorElementType(shapeInfo, &elementType), "GetTensorElementType");
        nuint rank = 0;
        OrtApiNative.Check(_api.GetDimensionsCount(shapeInfo, &rank), "GetDimensionsCount");
        var dimensions = new long[(int)rank];
        fixed (long* dims = dimensions)
        {
            OrtApiNative.Check(_api.GetDimensions(shapeInfo, dims, rank), "GetDimensions");
        }

        return (elementType, dimensions);
    }

    private void ReleasePinnedInputs()
    {
        foreach (var handle in _pinnedInputs)
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        _pinnedInputs.Clear();
    }

    private static void FreeNamePtrs(byte*[] ptrs)
    {
        foreach (var ptr in ptrs)
        {
            Marshal.FreeCoTaskMem((nint)ptr);
        }
    }

    private static void ValidateSessionContract(OrtApiTable api, nint session, OrtSessionContract contract)
    {
        ValidateSpecs(contract.Inputs, InspectSessionTensors(api, session, inputs: true), "input");
        ValidateSpecs(contract.Outputs, InspectSessionTensors(api, session, inputs: false), "output");
    }

    private static List<OrtTensorSpec> InspectSessionTensors(OrtApiTable api, nint session, bool inputs)
    {
        nuint count = 0;
        if (inputs)
        {
            OrtApiNative.Check(api.SessionGetInputCount(session, &count), "SessionGetInputCount");
        }
        else
        {
            OrtApiNative.Check(api.SessionGetOutputCount(session, &count), "SessionGetOutputCount");
        }

        nint allocator = 0;
        OrtApiNative.Check(api.GetAllocatorWithDefaultOptions(&allocator), "GetAllocatorWithDefaultOptions");
        var specs = new List<OrtTensorSpec>((int)count);
        for (nuint index = 0; index < count; index++)
        {
            nint namePtr = 0;
            if (inputs)
            {
                OrtApiNative.Check(api.SessionGetInputName(session, index, allocator, &namePtr), "SessionGetInputName");
            }
            else
            {
                OrtApiNative.Check(api.SessionGetOutputName(session, index, allocator, &namePtr), "SessionGetOutputName");
            }

            var name = Marshal.PtrToStringUTF8(namePtr)
                ?? throw new InvalidOperationException("Session tensor name is null.");
            OrtApiNative.Check(api.AllocatorFree(allocator, namePtr), "AllocatorFree");

            nint typeInfo = 0;
            if (inputs)
            {
                OrtApiNative.Check(api.SessionGetInputTypeInfo(session, index, &typeInfo), "SessionGetInputTypeInfo");
            }
            else
            {
                OrtApiNative.Check(api.SessionGetOutputTypeInfo(session, index, &typeInfo), "SessionGetOutputTypeInfo");
            }

            try
            {
                OrtOnnxType onnxType = OrtOnnxType.Unknown;
                OrtApiNative.Check(api.GetOnnxTypeFromTypeInfo(typeInfo, &onnxType), "GetOnnxTypeFromTypeInfo");
                if (onnxType != OrtOnnxType.Tensor)
                {
                    throw new InvalidOperationException($"{name} is not a tensor.");
                }

                nint tensorInfo = 0;
                OrtApiNative.Check(api.CastTypeInfoToTensorInfo(typeInfo, &tensorInfo), "CastTypeInfoToTensorInfo");
                var (elementType, dimensions) = ReadStaticTensorMetadata(api, tensorInfo);
                specs.Add(new OrtTensorSpec
                {
                    Name = name,
                    ElementType = MapDataKind(elementType),
                    Dimensions = dimensions,
                });
            }
            finally
            {
                if (typeInfo != 0)
                {
                    api.ReleaseTypeInfo(typeInfo);
                }
            }
        }

        return specs;
    }

    private static (OrtTensorElementType ElementType, long[] Dimensions) ReadStaticTensorMetadata(
        OrtApiTable api,
        nint tensorInfo)
    {
        OrtTensorElementType elementType = OrtTensorElementType.Undefined;
        OrtApiNative.Check(api.GetTensorElementType(tensorInfo, &elementType), "GetTensorElementType");
        nuint rank = 0;
        OrtApiNative.Check(api.GetDimensionsCount(tensorInfo, &rank), "GetDimensionsCount");
        var dimensions = new long[(int)rank];
        fixed (long* dims = dimensions)
        {
            OrtApiNative.Check(api.GetDimensions(tensorInfo, dims, rank), "GetDimensions");
        }

        return (elementType, dimensions);
    }

    private static void ValidateSpecs(
        IReadOnlyList<OrtTensorSpec> expected,
        IReadOnlyList<OrtTensorSpec> actual,
        string kind)
    {
        if (expected.Count != actual.Count)
        {
            throw new InvalidOperationException(
                $"{kind} count mismatch: expected {expected.Count}, got {actual.Count}.");
        }

        foreach (var spec in expected)
        {
            var match = actual.FirstOrDefault(a => a.Name == spec.Name)
                ?? throw new InvalidOperationException($"Missing model {kind} {spec.Name}.");
            if (match.ElementType != spec.ElementType)
            {
                throw new InvalidOperationException(
                    $"Model {kind} {spec.Name} type mismatch: expected {spec.ElementType}, got {match.ElementType}.");
            }

            if (!ShapeMatches(spec.Dimensions, match.Dimensions))
            {
                throw new InvalidOperationException(
                    $"Model {kind} {spec.Name} shape mismatch: expected [{string.Join(',', spec.Dimensions)}], " +
                    $"got [{string.Join(',', match.Dimensions)}].");
            }
        }
    }

    private static void ValidateInputs(IReadOnlyList<OrtTensorSpec> expected, IReadOnlyList<OrtTensorInput> inputs)
    {
        if (expected.Count != inputs.Count)
        {
            throw new InvalidOperationException(
                $"Input count mismatch: expected {expected.Count}, got {inputs.Count}.");
        }

        if (inputs.Select(i => i.Name).Distinct(StringComparer.Ordinal).Count() != inputs.Count)
        {
            throw new InvalidOperationException("Duplicate input names are not allowed.");
        }

        foreach (var spec in expected)
        {
            var found = false;
            var input = default(OrtTensorInput);
            foreach (var candidate in inputs)
            {
                if (candidate.Name == spec.Name)
                {
                    input = candidate;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException($"Missing input {spec.Name}.");
            }
            if (spec.ElementType != input.Kind)
            {
                throw new InvalidOperationException(
                    $"Input {spec.Name} type mismatch: expected {spec.ElementType}, got {input.Kind}.");
            }

            if (!ShapeMatches(spec.Dimensions, input.Dimensions))
            {
                throw new InvalidOperationException(
                    $"Input {spec.Name} shape mismatch: expected [{string.Join(',', spec.Dimensions)}], " +
                    $"got [{string.Join(',', input.Dimensions)}].");
            }
        }
    }

    private static bool ShapeMatches(IReadOnlyList<long> expected, IReadOnlyList<long> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            var a = actual[i];
            if (e >= 0 && a >= 0 && e != a)
            {
                return false;
            }
        }

        return true;
    }

    private static int ElementCount(IReadOnlyList<long> dimensions)
    {
        long count = 1;
        foreach (var dimension in dimensions)
        {
            if (dimension < 0)
            {
                throw new InvalidOperationException("Cannot compute element count for dynamic dimensions.");
            }

            count = checked(count * dimension);
        }

        return (int)count;
    }

    private static OrtTensorDataKind MapDataKind(OrtTensorElementType elementType) =>
        elementType switch
        {
            OrtTensorElementType.Float => OrtTensorDataKind.Float32,
            OrtTensorElementType.Int32 => OrtTensorDataKind.Int32,
            _ => throw new InvalidOperationException($"Unsupported tensor element type {elementType}."),
        };

    private static bool MapsTo(OrtTensorDataKind expected, OrtTensorElementType actual) =>
        expected switch
        {
            OrtTensorDataKind.Float32 => actual == OrtTensorElementType.Float,
            OrtTensorDataKind.Int32 => actual == OrtTensorElementType.Int32,
            _ => false,
        };
}
