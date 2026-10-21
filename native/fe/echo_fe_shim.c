/* Exported C ABI for Echo's P/Invoke. Wraps faster-enhancer.c (MIT). */
#include "fe.h"
#if defined(_WIN32)
#  define EXPORT __declspec(dllexport)
#else
#  define EXPORT __attribute__((visibility("default")))
#endif
EXPORT int  echo_fe_init(const void *w, int n) { return fe_init(w, n); }
EXPORT void echo_fe_run(const float *in, float *out) { fe_run(in, out); }
EXPORT void echo_fe_reset(void) { fe_reset(); }
EXPORT void echo_fe_free(void) { fe_free(); }
EXPORT int  echo_fe_frame_size(void) { return FE_FRAME_SIZE; }
EXPORT int  echo_fe_sample_rate(void) { return FE_SAMPLE_RATE; }
int fe_qgemm_force_tier(const char *tier_name);
EXPORT int  echo_fe_force_tier(const char *t) { return fe_qgemm_force_tier(t); }
