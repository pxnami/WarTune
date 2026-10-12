#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <sddl.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <stdio.h>

typedef struct Effect Effect;
typedef intptr_t (*Host)(Effect*, int32_t, int32_t, intptr_t, void*, float);
typedef intptr_t (*Dispatch)(Effect*, int32_t, int32_t, intptr_t, void*, float);
typedef void (*Process)(Effect*, float**, float**, int32_t);
struct Effect {
    int32_t magic; Dispatch dispatcher; Process process;
    void (*setParameter)(Effect*, int32_t, float);
    float (*getParameter)(Effect*, int32_t);
    int32_t programs, parameters, inputs, outputs, flags;
    intptr_t reserved1, reserved2;
    int32_t delay, quality1, quality2; float ioRatio;
    void *object, *user; int32_t uniqueID, version;
    Process processReplacing;
    void (*processDoubleReplacing)(Effect*, double**, double**, int32_t);
    char future[56];
};
typedef struct { uint64_t tick, frames; float peak, reduction; } Telemetry;
typedef struct {
    Effect effect; float parameters[6]; double rate, envelope, lowEnvelope, low[2];
    HANDLE mapping; Telemetry *telemetry;
} State;
static const char *names[] = {"Thresh", "Ratio", "Makeup", "Bass", "Multi", "Token"};
static double bounded(double x, double a, double b) { return x < a ? a : x > b ? b : x; }
static void setParameter(Effect *e, int32_t i, float value) {
    if (i >= 0 && i < 6 && isfinite(value)) ((State*)e->object)->parameters[i] = (float)bounded(value, 0, 1);
}
static float getParameter(Effect *e, int32_t i) { return i >= 0 && i < 6 ? ((State*)e->object)->parameters[i] : 0; }
static void beginTelemetry(State *s) {
    if (s->mapping) return;
    PSECURITY_DESCRIPTOR sd = NULL;
    if (ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:(A;;GA;;;SY)(A;;GA;;;LS)(A;;GA;;;NS)(A;;GR;;;WD)", SDDL_REVISION_1, &sd, NULL)) {
        SECURITY_ATTRIBUTES attributes = {sizeof(attributes), sd, FALSE};
        wchar_t name[64]; swprintf(name, 64, L"Global\\ArtIsWarAudio-DSP-%08X", (unsigned)lround(s->parameters[5] * 1048575.0));
        s->mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, &attributes, PAGE_READWRITE, 0, sizeof(Telemetry), name);
        LocalFree(sd);
        if (s->mapping) s->telemetry = MapViewOfFile(s->mapping, FILE_MAP_WRITE, 0, 0, sizeof(Telemetry));
    }
}
static double gainFor(double level, double threshold, double ratio) {
    if (level <= threshold) return 1;
    return pow(threshold / level, 1 - 1 / ratio);
}
static void processAudio(Effect *e, float **in, float **out, int32_t count) {
    State *s = e->object;
    double threshold = pow(10, (-36 + 30 * s->parameters[0]) / 20);
    double ratio = 1 + 11 * s->parameters[1];
    double makeup = pow(10, 12 * s->parameters[2] / 20);
    double release = exp(-1 / (.12 * s->rate));
    double crossover = 1 - exp(-2 * 3.141592653589793 * 180 / s->rate);
    double peak = 0, minGain = 1;
    for (int32_t i = 0; i < count; i++) {
        double x[2];
        for (int c = 0; c < 2; c++) { x[c] = isfinite(in[c][i]) ? in[c][i] : 0; s->low[c] += crossover * (x[c] - s->low[c]); }
        double detector = fmax(fabs(x[0]), fabs(x[1]));
        s->envelope = fmax(detector, s->envelope * release);
        double gain = gainFor(s->envelope, threshold, ratio);
        if (gain < minGain) minGain = gain;
        double lowPeak = fmax(fabs(s->low[0]), fabs(s->low[1]));
        s->lowEnvelope = fmax(lowPeak, s->lowEnvelope * release);
        double lowGain = s->parameters[4] >= .5f ? gainFor(s->lowEnvelope, .1 + .25 * (1 - s->parameters[3]), 1 + 7 * s->parameters[3]) : 1;
        for (int c = 0; c < 2; c++) x[c] = (x[c] + s->low[c] * (lowGain - 1)) * gain * makeup;
        double linkedPeak = fmax(fabs(x[0]), fabs(x[1]));
        double limit = linkedPeak > .891250938 ? .891250938 / linkedPeak : 1;
        for (int c = 0; c < 2; c++) { out[c][i] = (float)(x[c] * limit); peak = fmax(peak, fabs(out[c][i])); }
    }
    if (s->telemetry) {
        s->telemetry->peak = (float)peak;
        s->telemetry->reduction = (float)(-20 * log10(fmax(minGain, 1e-9)));
        s->telemetry->frames += count;
        InterlockedExchange64((volatile LONG64*)&s->telemetry->tick, (LONG64)GetTickCount64());
    }
}
static intptr_t dispatcher(Effect *e, int32_t op, int32_t index, intptr_t value, void *pointer, float option) {
    State *s = e->object;
    switch (op) {
    case 0: return 1;
    case 1:
        if (s->telemetry) UnmapViewOfFile(s->telemetry);
        if (s->mapping) CloseHandle(s->mapping);
        free(s); return 1;
    case 2: return 0;
    case 3: return 0;
    case 4: if (pointer) strncpy(pointer, "ArtIsWar", 24); return 1;
    case 6: if (pointer) strcpy(pointer, ""); return 1;
    case 7: if (pointer && index >= 0 && index < 6) snprintf(pointer, 8, "%.2f", s->parameters[index]); return 1;
    case 8: if (pointer && index >= 0 && index < 6) strncpy(pointer, names[index], 8); return 1;
    case 10: if (option >= 8000 && option <= 192000) s->rate = option; return 1;
    case 11: return 1;
    case 12: s->envelope = s->lowEnvelope = s->low[0] = s->low[1] = 0; if (value) beginTelemetry(s); return 1;
    case 35: return 1;
    case 45: if (pointer) strcpy(pointer, "ArtIsWar Dynamics"); return 1;
    case 47: if (pointer) strcpy(pointer, "ArtIsWar Audio"); return 1;
    case 48: if (pointer) strcpy(pointer, "ArtIsWar Dynamics"); return 1;
    case 49: return 1000;
    case 51: return 0;
    case 58: return 2400;
    default: (void)value; return 0;
    }
}
__declspec(dllexport) Effect *VSTPluginMain(Host host) {
    (void)host;
    State *s = calloc(1, sizeof(State));
    if (!s) return NULL;
    s->rate = 48000; s->parameters[0] = .35f; s->parameters[1] = .6f; s->parameters[2] = .4f;
    Effect *e = &s->effect;
    e->magic = 0x56737450; e->dispatcher = dispatcher; e->process = processAudio;
    e->setParameter = setParameter; e->getParameter = getParameter;
    e->programs = 1; e->parameters = 6; e->inputs = e->outputs = 2; e->flags = 16;
    e->object = s; e->uniqueID = 0x41495744; e->version = 1000; e->processReplacing = processAudio;
    return e;
}
#ifdef DSP_TEST
int main(int argc, char **argv) {
    HMODULE library = NULL;
    Effect *(*entry)(Host) = VSTPluginMain;
    if (argc > 1) {
        library = LoadLibraryA(argv[1]);
        if (!library) { puts("DLL load failed."); return 4; }
        entry = (Effect *(*)(Host))GetProcAddress(library, "VSTPluginMain");
        if (!entry) return 5;
    }
    Effect *e = entry(NULL);
    if (!e || e->magic != 0x56737450 || e->parameters != 6 || e->inputs != 2 || e->outputs != 2) return 6;
    for (int parameter = 0; parameter < 6; parameter++) {
        char name[8] = {0}; e->dispatcher(e, 8, parameter, 0, name, 0);
        if (strcmp(name, names[parameter]) != 0) return 7;
    }
    float left[4096], right[4096], a[4096], b[4096];
    float *input[] = {left, right}, *output[] = {a, b};
    for (int rate = 44100; rate <= 96000; rate += 3900) {
        e->dispatcher(e, 10, 0, 0, NULL, (float)rate);
        for (int multiband = 0; multiband < 2; multiband++) {
            e->setParameter(e, 4, (float)multiband);
            for (int i = 0; i < 4096; i++) { left[i] = (float)(5 * sin(i * .037)); right[i] = left[i] * .5f; }
            e->processReplacing(e, input, output, 4096);
            for (int i = 0; i < 4096; i++) if (!isfinite(a[i]) || fabs(a[i]) > .891252 || fabs(b[i] - a[i] * .5f) > .00001) return 1;
        }
    }
    e->dispatcher(e, 12, 0, 0, NULL, 0);
    memset(left, 0, sizeof(left)); memset(right, 0, sizeof(right)); left[0] = NAN; right[0] = INFINITY;
    e->processReplacing(e, input, output, 4096);
    for (int i = 0; i < 4096; i++) if (!isfinite(a[i]) || fabs(a[i]) > .000001) return 2;
    double quiet = 0, loud = 0;
    e->setParameter(e, 4, 0);
    for (int phase = 0; phase < 2; phase++) {
        e->dispatcher(e, 12, 0, 0, NULL, 0);
        double level = phase ? .8 : .005;
        for (int i = 0; i < 4096; i++) left[i] = right[i] = (float)(level * sin(i * .1));
        e->processReplacing(e, input, output, 4096);
        double energy = 0; for (int i = 2048; i < 4096; i++) energy += a[i] * a[i];
        if (phase) loud = sqrt(energy / 2048); else quiet = sqrt(energy / 2048);
    }
    if (quiet <= .003535 || loud / quiet >= 160) return 3;
    e->dispatcher(e, 1, 0, 0, NULL, 0);
    if (library) FreeLibrary(library);
    puts("PASS: peak ceiling, stereo linking, sample rates, silence, non-finite input, quiet gain and dynamic range reduction.");
    return 0;
}
#endif
