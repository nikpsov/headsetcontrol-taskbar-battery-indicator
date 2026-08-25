#include <string>
#include <cstdio>
#include <cstring>
#include "hidapi.h"
#include "headsetcontrol_c.h"

extern "C" {

HSC_API int hsc_dump_hid_devices(char* buffer, int max_len)
{
    if (!buffer || max_len <= 0) return 0;
    buffer[0] = '\0';

    hid_device_info* devs = hid_enumerate(0, 0);
    std::string out;
    for (hid_device_info* cur = devs; cur; cur = cur->next) {
        char line[256];
        snprintf(line, sizeof(line), "  [HID] VID: 0x%04X, PID: 0x%04X, Mfr: '%ls', Prod: '%ls', Usage: 0x%04X:0x%04X\n",
            cur->vendor_id, cur->product_id,
            cur->manufacturer_string ? cur->manufacturer_string : L"",
            cur->product_string ? cur->product_string : L"",
            cur->usage_page, cur->usage);
        out += line;
    }
    hid_free_enumeration(devs);

    strncpy(buffer, out.c_str(), max_len - 1);
    buffer[max_len - 1] = '\0';
    return static_cast<int>(out.length());
}

}
