// A thin, flat C bridge from the service to AMD ADLX GPU tuning. The service P/Invokes these functions
// (src/Elynt.Windows/AdlxGpuTuning.cs); the ABI is ours, so a wrong ADLX vtable can never be called from C#.
//
// Build (x64 Native Tools prompt, with https://github.com/GPUOpen-LibrariesAndSDKs/ADLX checked out at %ADLX%):
//   cl /nologo /O2 /EHsc /LD /std:c++17 /I"%ADLX%" ElyntAdlx.cpp "%ADLX%\SDK\ADLXHelper\Windows\Cpp\ADLXHelper.cpp" ^
//      "%ADLX%\SDK\Platform\Windows\WinAPIs.cpp" /Fe:elynt_adlx.dll
// and put elynt_adlx.dll next to Elynt.Service.exe. ADLX itself (amdadlx64.dll) ships with the AMD driver.
// AMD's ADLX SDK licence allows shipping this only in object-code form, inside this app.
//
// Every function returns 0 on success, -1 when not supported, or another ADLX_RESULT error code. Nothing here is verified on real hardware yet.

#include "SDK/ADLXHelper/Windows/Cpp/ADLXHelper.h"
#include "SDK/Include/IGPUAutoTuning.h"
#include "SDK/Include/IGPUManualFanTuning.h"
#include "SDK/Include/IGPUManualGFXTuning.h"
#include "SDK/Include/IGPUManualPowerTuning.h"
#include "SDK/Include/IGPUManualVRAMTuning.h"
#include "SDK/Include/IGPUTuning.h"

#include <windows.h>
#include <cstring>

using namespace adlx;

namespace
{
    ADLXHelper g_adlx;
    IADLXGPUTuningServicesPtr g_tuning;
    IADLXGPUListPtr g_gpus;

    enum Control { MinClock = 0, MaxClock = 1, Voltage = 2, VramMaxClock = 3, PowerLimit = 4, FanSpeed = 5 };
    enum AutoKind { OverclockGpu = 0, UndervoltGpu = 1, OverclockVram = 2 };

    struct Range { int min; int max; int step; int current; };

    ADLX_RESULT Gpu(int index, IADLXGPUPtr& gpu)
    {
        if (!g_gpus || index < 0 || static_cast<adlx_uint>(index) >= g_gpus->Size())
            return ADLX_INVALID_ARGS;
        return g_gpus->At(static_cast<adlx_uint>(index), &gpu);
    }

    ADLX_RESULT Fill(ADLX_RESULT rangeRes, const ADLX_IntRange& range, ADLX_RESULT currentRes, const adlx_int& current, Range* out)
    {
        if (ADLX_FAILED(rangeRes)) return rangeRes;
        if (ADLX_FAILED(currentRes)) return currentRes;
        out->min = range.minValue;
        out->max = range.maxValue;
        out->step = range.step > 0 ? range.step : 1;
        out->current = current;
        return ADLX_OK;
    }

    int Code(ADLX_RESULT res)
    {
        if (ADLX_SUCCEEDED(res)) return 0;
        return res == ADLX_NOT_SUPPORTED ? -1 : static_cast<int>(res);
    }

    class AutoTuningDone : public IADLXGPUAutoTuningCompleteListener
    {
    public:
        HANDLE done = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        ~AutoTuningDone() { CloseHandle(done); }
        adlx_bool ADLX_STD_CALL OnGPUAutoTuningComplete(IADLXGPUAutoTuningCompleteEvent*) override
        {
            SetEvent(done);
            return true;
        }
    };
}

extern "C"
{
    static ADLX_RESULT impl_elynt_adlx_init()
    {
        ADLX_RESULT res = g_adlx.Initialize();
        if (ADLX_FAILED(res)) return res;
        IADLXSystem* system = g_adlx.GetSystemServices();
        res = system->GetGPUTuningServices(&g_tuning);
        if (ADLX_FAILED(res)) return res;
        return system->GetGPUs(&g_gpus);
    }

    __declspec(dllexport) void elynt_adlx_terminate()
    {
        g_gpus = nullptr;
        g_tuning = nullptr;
        g_adlx.Terminate();
    }

    __declspec(dllexport) int elynt_adlx_gpu_count()
    {
        return g_gpus ? static_cast<int>(g_gpus->Size()) : 0;
    }

    static ADLX_RESULT impl_elynt_adlx_gpu_name(int index, char* buffer, int length)
    {
        IADLXGPUPtr gpu;
        ADLX_RESULT res = Gpu(index, gpu);
        if (ADLX_FAILED(res)) return res;
        const char* name = nullptr;
        res = gpu->Name(&name);
        if (ADLX_FAILED(res)) return res;
        strncpy_s(buffer, length, name ? name : "AMD GPU", _TRUNCATE);
        return ADLX_OK;
    }

    static ADLX_RESULT impl_elynt_adlx_gpu_unique_id(int index, int* id)
    {
        IADLXGPUPtr gpu;
        ADLX_RESULT res = Gpu(index, gpu);
        if (ADLX_FAILED(res)) return res;
        adlx_int value = 0;
        res = gpu->UniqueId(&value);
        *id = value;
        return res;
    }

    // Reads one control's driver-reported range and current value. Returns ADLX_NOT_SUPPORTED when the card lacks it.
    static ADLX_RESULT impl_elynt_adlx_read(int index, int control, Range* out)
    {
        IADLXGPUPtr gpu;
        ADLX_RESULT res = Gpu(index, gpu);
        if (ADLX_FAILED(res)) return res;
        adlx_bool supported = false;
        IADLXInterfacePtr ifc;
        ADLX_IntRange range = {};
        adlx_int current = 0;

        switch (control)
        {
        case MinClock:
        case MaxClock:
        case Voltage:
        {
            res = g_tuning->IsSupportedManualGFXTuning(gpu, &supported);
            if (ADLX_FAILED(res) || !supported) return ADLX_NOT_SUPPORTED;
            res = g_tuning->GetManualGFXTuning(gpu, &ifc);
            if (ADLX_FAILED(res)) return res;
            IADLXManualGraphicsTuning2Ptr gfx(ifc); // RDNA2 and newer; older cards use frequency states
            if (!gfx) return ADLX_NOT_SUPPORTED;
            if (control == MinClock)
                return Fill(gfx->GetGPUMinFrequencyRange(&range), range, gfx->GetGPUMinFrequency(&current), current, out);
            if (control == MaxClock)
                return Fill(gfx->GetGPUMaxFrequencyRange(&range), range, gfx->GetGPUMaxFrequency(&current), current, out);
            return Fill(gfx->GetGPUVoltageRange(&range), range, gfx->GetGPUVoltage(&current), current, out);
        }
        case VramMaxClock:
        {
            res = g_tuning->IsSupportedManualVRAMTuning(gpu, &supported);
            if (ADLX_FAILED(res) || !supported) return ADLX_NOT_SUPPORTED;
            res = g_tuning->GetManualVRAMTuning(gpu, &ifc);
            if (ADLX_FAILED(res)) return res;
            IADLXManualVRAMTuning2Ptr vram(ifc);
            if (!vram) return ADLX_NOT_SUPPORTED;
            return Fill(vram->GetMaxVRAMFrequencyRange(&range), range, vram->GetMaxVRAMFrequency(&current), current, out);
        }
        case PowerLimit:
        {
            res = g_tuning->IsSupportedManualPowerTuning(gpu, &supported);
            if (ADLX_FAILED(res) || !supported) return ADLX_NOT_SUPPORTED;
            res = g_tuning->GetManualPowerTuning(gpu, &ifc);
            if (ADLX_FAILED(res)) return res;
            IADLXManualPowerTuningPtr power(ifc);
            if (!power) return ADLX_NOT_SUPPORTED;
            return Fill(power->GetPowerLimitRange(&range), range, power->GetPowerLimit(&current), current, out);
        }
        case FanSpeed:
        {
            res = g_tuning->IsSupportedManualFanTuning(gpu, &supported);
            if (ADLX_FAILED(res) || !supported) return ADLX_NOT_SUPPORTED;
            res = g_tuning->GetManualFanTuning(gpu, &ifc);
            if (ADLX_FAILED(res)) return res;
            IADLXManualFanTuningPtr fan(ifc);
            if (!fan) return ADLX_NOT_SUPPORTED;
            adlx_bool target = false;
            if (ADLX_FAILED(fan->IsSupportedTargetFanSpeed(&target)) || !target) return ADLX_NOT_SUPPORTED;
            return Fill(fan->GetTargetFanSpeedRange(&range), range, fan->GetTargetFanSpeed(&current), current, out);
        }
        default:
            return ADLX_INVALID_ARGS;
        }
    }

    static ADLX_RESULT impl_elynt_adlx_write(int index, int control, int value)
    {
        IADLXGPUPtr gpu;
        ADLX_RESULT res = Gpu(index, gpu);
        if (ADLX_FAILED(res)) return res;
        IADLXInterfacePtr ifc;
        switch (control)
        {
        case MinClock:
        case MaxClock:
        case Voltage:
        {
            res = g_tuning->GetManualGFXTuning(gpu, &ifc);
            if (ADLX_FAILED(res)) return res;
            IADLXManualGraphicsTuning2Ptr gfx(ifc);
            if (!gfx) return ADLX_NOT_SUPPORTED;
            if (control == MinClock) return gfx->SetGPUMinFrequency(value);
            if (control == MaxClock) return gfx->SetGPUMaxFrequency(value);
            return gfx->SetGPUVoltage(value);
        }
        case VramMaxClock:
        {
            res = g_tuning->GetManualVRAMTuning(gpu, &ifc);
            if (ADLX_FAILED(res)) return res;
            IADLXManualVRAMTuning2Ptr vram(ifc);
            return vram ? vram->SetMaxVRAMFrequency(value) : ADLX_NOT_SUPPORTED;
        }
        case PowerLimit:
        {
            res = g_tuning->GetManualPowerTuning(gpu, &ifc);
            if (ADLX_FAILED(res)) return res;
            IADLXManualPowerTuningPtr power(ifc);
            return power ? power->SetPowerLimit(value) : ADLX_NOT_SUPPORTED;
        }
        case FanSpeed:
        {
            res = g_tuning->GetManualFanTuning(gpu, &ifc);
            if (ADLX_FAILED(res)) return res;
            IADLXManualFanTuningPtr fan(ifc);
            return fan ? fan->SetTargetFanSpeed(value) : ADLX_NOT_SUPPORTED;
        }
        default:
            return ADLX_INVALID_ARGS;
        }
    }

    static ADLX_RESULT impl_elynt_adlx_is_at_factory(int index, int* atFactory)
    {
        IADLXGPUPtr gpu;
        ADLX_RESULT res = Gpu(index, gpu);
        if (ADLX_FAILED(res)) return res;
        adlx_bool value = false;
        res = g_tuning->IsAtFactory(gpu, &value);
        *atFactory = value ? 1 : 0;
        return res;
    }

    static ADLX_RESULT impl_elynt_adlx_reset(int index)
    {
        IADLXGPUPtr gpu;
        ADLX_RESULT res = Gpu(index, gpu);
        if (ADLX_FAILED(res)) return res;
        return g_tuning->ResetToFactory(gpu);
    }

    static ADLX_RESULT impl_elynt_adlx_auto_supported(int index, int kind, int* supportedOut)
    {
        *supportedOut = 0;
        IADLXGPUPtr gpu;
        ADLX_RESULT res = Gpu(index, gpu);
        if (ADLX_FAILED(res)) return res;
        adlx_bool supported = false;
        res = g_tuning->IsSupportedAutoTuning(gpu, &supported);
        if (ADLX_FAILED(res) || !supported) return ADLX_OK;
        IADLXInterfacePtr ifc;
        res = g_tuning->GetAutoTuning(gpu, &ifc);
        if (ADLX_FAILED(res)) return res;
        IADLXGPUAutoTuningPtr autoTuning(ifc);
        if (!autoTuning) return ADLX_OK;
        if (kind == OverclockGpu) res = autoTuning->IsSupportedOverclockGPU(&supported);
        else if (kind == UndervoltGpu) res = autoTuning->IsSupportedUndervoltGPU(&supported);
        else if (kind == OverclockVram) res = autoTuning->IsSupportedOverclockVRAM(&supported);
        else return ADLX_INVALID_ARGS;
        *supportedOut = ADLX_SUCCEEDED(res) && supported ? 1 : 0;
        return ADLX_OK;
    }

    // Starts the driver's auto tuning and waits for it to finish, up to timeoutMs.
    static ADLX_RESULT impl_elynt_adlx_auto(int index, int kind, int timeoutMs)
    {
        IADLXGPUPtr gpu;
        ADLX_RESULT res = Gpu(index, gpu);
        if (ADLX_FAILED(res)) return res;
        IADLXInterfacePtr ifc;
        res = g_tuning->GetAutoTuning(gpu, &ifc);
        if (ADLX_FAILED(res)) return res;
        IADLXGPUAutoTuningPtr autoTuning(ifc);
        if (!autoTuning) return ADLX_NOT_SUPPORTED;

        AutoTuningDone listener;
        if (kind == OverclockGpu) res = autoTuning->StartOverclockGPU(&listener);
        else if (kind == UndervoltGpu) res = autoTuning->StartUndervoltGPU(&listener);
        else if (kind == OverclockVram) res = autoTuning->StartOverclockVRAM(&listener);
        else return ADLX_INVALID_ARGS;
        if (ADLX_FAILED(res)) return res;
        return WaitForSingleObject(listener.done, static_cast<DWORD>(timeoutMs)) == WAIT_OBJECT_0 ? ADLX_OK : ADLX_FAIL;
    }

    // Exported entry points: 0 on success, -1 when the card or driver doesn't support it, otherwise the ADLX_RESULT.
    __declspec(dllexport) int elynt_adlx_init() { return Code(impl_elynt_adlx_init()); }
    __declspec(dllexport) int elynt_adlx_gpu_name(int index, char* buffer, int length) { return Code(impl_elynt_adlx_gpu_name(index, buffer, length)); }
    __declspec(dllexport) int elynt_adlx_gpu_unique_id(int index, int* id) { return Code(impl_elynt_adlx_gpu_unique_id(index, id)); }
    __declspec(dllexport) int elynt_adlx_read(int index, int control, Range* out) { return Code(impl_elynt_adlx_read(index, control, out)); }
    __declspec(dllexport) int elynt_adlx_write(int index, int control, int value) { return Code(impl_elynt_adlx_write(index, control, value)); }
    __declspec(dllexport) int elynt_adlx_is_at_factory(int index, int* atFactory) { return Code(impl_elynt_adlx_is_at_factory(index, atFactory)); }
    __declspec(dllexport) int elynt_adlx_reset(int index) { return Code(impl_elynt_adlx_reset(index)); }
    __declspec(dllexport) int elynt_adlx_auto_supported(int index, int kind, int* supportedOut) { return Code(impl_elynt_adlx_auto_supported(index, kind, supportedOut)); }
    __declspec(dllexport) int elynt_adlx_auto(int index, int kind, int timeoutMs) { return Code(impl_elynt_adlx_auto(index, kind, timeoutMs)); }
}
