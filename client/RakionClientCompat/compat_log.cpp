#include <windows.h>

#include <fstream>
#include <string>

#include "compat_log.h"

void CompatLog(const char* message)
{
    char temp[MAX_PATH]{};
    if (GetTempPathA(MAX_PATH, temp) == 0) return;
    std::ofstream out(std::string(temp) + "rakion_client_compat.log", std::ios::app);
    out << message << '\n';
}
