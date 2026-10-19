// A thin, flat C bridge from the service to Intel IGCL (Intel Graphics Control Library) GPU tuning. The service
// P/Invokes these functions (src/Elynt.Windows/IgclGpuTuning.cs); the ABI is ours, so IGCL's structs and versioning
// never cross into C#.
//
// Build (x64 Native Tools prompt, with https://github.com/intel/drivers.gpu.control-library checked out at %IGCL%):
//   cl /nologo /O2 /EHsc /LD /std:c++17 /I"%IGCL%\include" ElyntIgcl.cpp "%IGCL%\Source\cApiWrapper.cpp" ^
//      /Fe:elynt_igcl.dll
// and put elynt_igcl.dll next to Elynt.Service.exe. IGCL itself (ControlLib.dll) ships with the Intel driver.
// Intel's licence allows redistributing the Control API software and headers solely for use on Intel platforms,
// with its notice kept.
//
// Controls: 0 GPU frequency offset, 1 GPU max voltage offset, 2 sustained power limit, 3 temperature limit, all
// through the V2 calls in the units ctlOverclockGetProperties reports. Every function returns 0 on success, -1 when
// not supported, or another ctl_result_t error code. Nothing here is verified on real hardware yet.

// Empty, so the IGCL wrapper's ctl* functions stay private to this DLL; only the elynt_igcl_* functions are exported.
#define CTL_APIEXPORT
#include <windows.h>
#include "igcl_api.h"

#include <cstring>
#include <vector>

namespace
{
    ctl_api_handle_t g_api = nullptr;
    std::vector<ctl_device_adapter_handle_t> g_gpus;

    enum Control { FrequencyOffset = 0, VoltageOffset = 1, PowerLimit = 2, TemperatureLimit = 3 };

    struct ControlInfo
    {
        int supported;
        int units;
        double min;
        double max;
        double step;
        double defaultValue;
    };

    int Code(ctl_result_t res)
    {
        if (res == CTL_RESULT_SUCCESS) return 0;
        return res == CTL_RESULT_ERROR_UNSUPPORTED_FEATURE ? -1 : static_cast<int>(res);
    }

    ctl_device_adapter_handle_t Gpu(int index)
    {
        return index >= 0 && static_cast<size_t>(index) < g_gpus.size() ? g_gpus[index] : nullptr;
    }

    ctl_result_t Properties(ctl_device_adapter_handle_t gpu, ctl_oc_properties_t& props)
    {
        std::memset(&props, 0, sizeof(props));
        props.Size = sizeof(props);
        props.Version = 1;
        return ctlOverclockGetProperties(gpu, &props);
    }
}

extern "C"
{
    __declspec(dllexport) int elynt_igcl_init()
    {
        ctl_init_args_t args;
        std::memset(&args, 0, sizeof(args));
        args.Size = sizeof(args);
        args.AppVersion = CTL_IMPL_VERSION;
        args.flags = 0;
        ctl_result_t res = ctlInit(&args, &g_api);
        if (res != CTL_RESULT_SUCCESS) return Code(res);

        uint32_t count = 0;
        res = ctlEnumerateDevices(g_api, &count, nullptr);
        if (res != CTL_RESULT_SUCCESS) return Code(res);
        std::vector<ctl_device_adapter_handle_t> all(count);
        if (count > 0)
        {
            res = ctlEnumerateDevices(g_api, &count, all.data());
            if (res != CTL_RESULT_SUCCESS) return Code(res);
        }

        // Keep only Intel graphics adapters that support overclocking at all.
        g_gpus.clear();
        for (auto gpu : all)
        {
            LUID luid{};
            ctl_device_adapter_properties_t props;
            std::memset(&props, 0, sizeof(props));
            props.Size = sizeof(props);
            props.pDeviceID = &luid;
            props.device_id_size = sizeof(luid);
            if (ctlGetDeviceProperties(gpu, &props) != CTL_RESULT_SUCCESS) continue;
            if (props.device_type != CTL_DEVICE_TYPE_GRAPHICS || props.pci_vendor_id != 0x8086) continue;
            ctl_oc_properties_t oc;
            if (Properties(gpu, oc) != CTL_RESULT_SUCCESS || !oc.bSupported) continue;
            g_gpus.push_back(gpu);
        }
        return 0;
    }

    __declspec(dllexport) void elynt_igcl_terminate()
    {
        g_gpus.clear();
        if (g_api) ctlClose(g_api);
        g_api = nullptr;
    }

    __declspec(dllexport) int elynt_igcl_gpu_count()
    {
        return static_cast<int>(g_gpus.size());
    }

    __declspec(dllexport) int elynt_igcl_gpu_name(int index, char* buffer, int length)
    {
        auto gpu = Gpu(index);
        if (!gpu || !buffer || length <= 0) return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        LUID luid{};
        ctl_device_adapter_properties_t props;
        std::memset(&props, 0, sizeof(props));
        props.Size = sizeof(props);
        props.pDeviceID = &luid;
        props.device_id_size = sizeof(luid);
        ctl_result_t res = ctlGetDeviceProperties(gpu, &props);
        if (res != CTL_RESULT_SUCCESS) return Code(res);
        strncpy_s(buffer, length, props.name, _TRUNCATE);
        return 0;
    }

    __declspec(dllexport) int elynt_igcl_gpu_luid(int index, long long* luidOut)
    {
        auto gpu = Gpu(index);
        if (!gpu || !luidOut) return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        LUID luid{};
        ctl_device_adapter_properties_t props;
        std::memset(&props, 0, sizeof(props));
        props.Size = sizeof(props);
        props.pDeviceID = &luid;
        props.device_id_size = sizeof(luid);
        ctl_result_t res = ctlGetDeviceProperties(gpu, &props);
        if (res != CTL_RESULT_SUCCESS) return Code(res);
        *luidOut = (static_cast<long long>(luid.HighPart) << 32) | luid.LowPart;
        return 0;
    }

    __declspec(dllexport) int elynt_igcl_control_info(int index, int control, ControlInfo* out)
    {
        auto gpu = Gpu(index);
        if (!gpu || !out) return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        ctl_oc_properties_t props;
        ctl_result_t res = Properties(gpu, props);
        if (res != CTL_RESULT_SUCCESS) return Code(res);

        const ctl_oc_control_info_t* info = nullptr;
        switch (control)
        {
        case FrequencyOffset: info = &props.gpuFrequencyOffset; break;
        case VoltageOffset: info = &props.gpuVoltageOffset; break;
        case PowerLimit: info = &props.powerLimit; break;
        case TemperatureLimit: info = &props.temperatureLimit; break;
        default: return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        }
        out->supported = info->bSupported ? 1 : 0;
        out->units = static_cast<int>(info->units);
        out->min = info->min;
        out->max = info->max;
        out->step = info->step;
        out->defaultValue = info->Default;
        return 0;
    }

    __declspec(dllexport) int elynt_igcl_read(int index, int control, double* value)
    {
        auto gpu = Gpu(index);
        if (!gpu || !value) return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        switch (control)
        {
        case FrequencyOffset: return Code(ctlOverclockGpuFrequencyOffsetGetV2(gpu, value));
        case VoltageOffset: return Code(ctlOverclockGpuMaxVoltageOffsetGetV2(gpu, value));
        case PowerLimit: return Code(ctlOverclockPowerLimitGetV2(gpu, value));
        case TemperatureLimit: return Code(ctlOverclockTemperatureLimitGetV2(gpu, value));
        default: return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        }
    }

    __declspec(dllexport) int elynt_igcl_write(int index, int control, double value)
    {
        auto gpu = Gpu(index);
        if (!gpu) return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        switch (control)
        {
        case FrequencyOffset: return Code(ctlOverclockGpuFrequencyOffsetSetV2(gpu, value));
        case VoltageOffset: return Code(ctlOverclockGpuMaxVoltageOffsetSetV2(gpu, value));
        case PowerLimit: return Code(ctlOverclockPowerLimitSetV2(gpu, value));
        case TemperatureLimit: return Code(ctlOverclockTemperatureLimitSetV2(gpu, value));
        default: return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        }
    }

    // Call only after the user accepted the app's warning; IGCL forgets it when this process ends.
    __declspec(dllexport) int elynt_igcl_waiver(int index)
    {
        auto gpu = Gpu(index);
        if (!gpu) return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        return Code(ctlOverclockWaiverSet(gpu));
    }

    __declspec(dllexport) int elynt_igcl_reset(int index)
    {
        auto gpu = Gpu(index);
        if (!gpu) return CTL_RESULT_ERROR_INVALID_ARGUMENT;
        return Code(ctlOverclockResetToDefault(gpu));
    }
}
