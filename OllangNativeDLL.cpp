#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>
#include <windows.h>


#pragma pack(push, 1)
struct DosHeader {
  uint16_t e_magic;
  uint8_t pad[58];
  uint32_t e_lfanew;
};
struct CoffHeader {
  uint32_t peSignature;
  uint16_t machine;
  uint16_t numSections;
  uint32_t timestamp;
  uint32_t symTablePtr;
  uint32_t numSymbols;
  uint16_t optHeaderSize;
  uint16_t characteristics;
};
struct OptHeader64 {
  uint16_t magic;
  uint8_t linkerMaj, linkerMin;
  uint32_t sizeOfCode;
  uint32_t sizeOfInitData;
  uint32_t sizeOfUninitData;
  uint32_t entryPointRva;
  uint32_t baseOfCode;
  uint64_t imageBase;
  uint32_t sectionAlign;
  uint32_t fileAlign;
  uint16_t osMaj, osMin;
  uint16_t imgMaj, imgMin;
  uint16_t subMaj, subMin;
  uint32_t win32Ver;
  uint32_t sizeOfImage;
  uint32_t sizeOfHeaders;
  uint32_t checksum;
  uint16_t subsystem;
  uint16_t dllChars;
  uint64_t stackReserve;
  uint64_t stackCommit;
  uint64_t heapReserve;
  uint64_t heapCommit;
  uint32_t loaderFlags;
  uint32_t numDataDirs;
};
struct DataDir {
  uint32_t rva;
  uint32_t size;
};
struct SectionHeader {
  char name[8];
  uint32_t virtualSize;
  uint32_t virtualAddress;
  uint32_t rawDataSize;
  uint32_t rawDataPtr;
  uint32_t relocPtr;
  uint32_t lineNumPtr;
  uint16_t numRelocs;
  uint16_t numLineNums;
  uint32_t characteristics;
};
#pragma pack(pop)

static uint32_t align_up(uint32_t val, uint32_t a) {
  return (val + a - 1) & ~(a - 1);
}

static int FindImportDir(const std::vector<uint8_t> &bin, uint32_t sectRva) {
  const int ENTRY_SIZE = 20;
  for (int i = (int)bin.size() - ENTRY_SIZE; i >= ENTRY_SIZE * 2; i--) {
    bool allZero = true;
    for (int j = 0; j < ENTRY_SIZE; j++) {
      if (bin[i + j] != 0) {
        allZero = false;
        break;
      }
    }
    if (!allZero)
      continue;

    int entry2Start = i - ENTRY_SIZE;
    uint32_t ilt2 = *(uint32_t *)&bin[entry2Start];
    uint32_t name2 = *(uint32_t *)&bin[entry2Start + 12];
    if (ilt2 < sectRva || ilt2 > sectRva + bin.size())
      continue;
    if (name2 < sectRva || name2 > sectRva + bin.size())
      continue;

    int entry1Start = entry2Start - ENTRY_SIZE;
    if (entry1Start < 0)
      continue;
    uint32_t ilt1 = *(uint32_t *)&bin[entry1Start];
    uint32_t name1 = *(uint32_t *)&bin[entry1Start + 12];
    if (ilt1 < sectRva || ilt1 > sectRva + bin.size())
      continue;
    if (name1 < sectRva || name1 > sectRva + bin.size())
      continue;

    int nameOff1 = name1 - sectRva;
    int nameOff2 = name2 - sectRva;
    if (nameOff1 >= 0 && nameOff1 < (int)bin.size() - 12 && nameOff2 >= 0 &&
        nameOff2 < (int)bin.size() - 10) {
      if (memcmp(&bin[nameOff1], "kernel32.dll", 12) == 0 ||
          memcmp(&bin[nameOff1], "msvcrt.dll", 10) == 0 ||
          memcmp(&bin[nameOff2], "kernel32.dll", 12) == 0 ||
          memcmp(&bin[nameOff2], "msvcrt.dll", 10) == 0) {
        return entry1Start;
      }
    }
  }
  return -1;
}

extern "C" __declspec(dllexport) bool
CompileNative(const char *nasmPath, const char *asmCode, const char *outPath, int importDirFileOff, int importDirSize, char *errorMsg, int maxErr) {
  if (!asmCode || !outPath || !nasmPath)
    return false;

  std::string tmpAsm = "ollang_native_temp.asm";
  std::string tmpBin = "ollang_native_temp.bin";
  std::string errLog = "ollang_nasm_err.log";

  {
    std::ofstream f(tmpAsm, std::ios::binary);
    if (!f) {
      snprintf(errorMsg, maxErr, "Cannot create temp.asm");
      return false;
    }
    f << asmCode;
  }

  std::string cmd = std::string("\"") + nasmPath + "\" -f bin \"" + tmpAsm +
                    "\" -o \"" + tmpBin + "\" 2> \"" + errLog + "\"";
  int rc = system(cmd.c_str());
  if (rc != 0) {
    std::ifstream ef(errLog);
    std::string log((std::istreambuf_iterator<char>(ef)), std::istreambuf_iterator<char>());
    snprintf(errorMsg, maxErr, "NASM error:\n%s", log.c_str());
    return false;
  }

  std::ifstream bf(tmpBin, std::ios::binary | std::ios::ate);
  if (!bf) {
    snprintf(errorMsg, maxErr, "Cannot read assembled binary");
    return false;
  }
  size_t binSize = bf.tellg();
  bf.seekg(0);
  std::vector<uint8_t> bin(binSize);
  bf.read((char *)bin.data(), binSize);
  bf.close();

  if (binSize == 0) {
    snprintf(errorMsg, maxErr, "NASM produced empty output");
    return false;
  }

  const uint32_t IMAGE_BASE = 0x400000;
  const uint32_t SECT_ALIGN = 0x1000;
  const uint32_t FILE_ALIGN = 0x200;
  const uint32_t SECT_RVA = 0x1000;
  const uint32_t HEADER_SIZE = FILE_ALIGN;

  int impOff = importDirFileOff;
  if (impOff < 0) {
    impOff = FindImportDir(bin, SECT_RVA);
    if (impOff < 0) {
      snprintf(errorMsg, maxErr, "Could not locate import directory in assembled thing");
      return false;
    }
  }

  uint32_t importRva = SECT_RVA + (uint32_t)impOff;
  int impSize = (importDirSize > 0) ? importDirSize : 60;

  uint32_t rawSize = align_up((uint32_t)binSize, FILE_ALIGN);
  uint32_t virtSize = (uint32_t)binSize;
  uint32_t imageSize = SECT_RVA + align_up(virtSize, SECT_ALIGN);

  std::vector<uint8_t> exe(HEADER_SIZE + rawSize, 0);

  DosHeader *dos = (DosHeader *)exe.data();
  dos->e_magic = 0x5A4D;
  dos->e_lfanew = sizeof(DosHeader);

  CoffHeader *coff = (CoffHeader *)(exe.data() + dos->e_lfanew);
  coff->peSignature = 0x4550;
  coff->machine = 0x8664;
  coff->numSections = 1;
  coff->optHeaderSize = sizeof(OptHeader64) + 16 * sizeof(DataDir);
  coff->characteristics = 0x22;

  OptHeader64 *opt = (OptHeader64 *)((uint8_t *)coff + sizeof(CoffHeader));
  opt->magic = 0x20B;
  opt->sizeOfCode = rawSize;
  opt->entryPointRva = SECT_RVA;
  opt->baseOfCode = SECT_RVA;
  opt->imageBase = IMAGE_BASE;
  opt->sectionAlign = SECT_ALIGN;
  opt->fileAlign = FILE_ALIGN;
  opt->osMaj = 6;
  opt->subMaj = 6;
  opt->sizeOfImage = imageSize;
  opt->sizeOfHeaders = HEADER_SIZE;
  opt->subsystem = 3; // CONSOLE
  opt->dllChars = 0x8160;
  opt->stackReserve = 0x100000;
  opt->stackCommit = 0x1000;
  opt->heapReserve = 0x100000;
  opt->heapCommit = 0x1000;
  opt->numDataDirs = 16;

  DataDir *dirs = (DataDir *)((uint8_t *)opt + sizeof(OptHeader64));
  dirs[1].rva = importRva;
  dirs[1].size = (uint32_t)impSize;
  SectionHeader *sec = (SectionHeader *)((uint8_t *)dirs + 16 * sizeof(DataDir));
  memcpy(sec->name, ".ollang", 7);
  sec->virtualSize = virtSize;
  sec->virtualAddress = SECT_RVA;
  sec->rawDataSize = rawSize;
  sec->rawDataPtr = HEADER_SIZE;
  sec->characteristics = 0xE0000060;

  memcpy(exe.data() + HEADER_SIZE, bin.data(), binSize);

  std::ofstream out(outPath, std::ios::binary);
  if (!out) {
    snprintf(errorMsg, maxErr, "Cannot write output: %s", outPath);
    return false;
  }
  out.write((char *)exe.data(), exe.size());
  out.close();

  DeleteFileA(tmpAsm.c_str());
  DeleteFileA(tmpBin.c_str());
  DeleteFileA(errLog.c_str());

  return true;
}
