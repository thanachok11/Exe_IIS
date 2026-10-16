# IIS Keep-Alive Tool

โปรแกรม .NET 8 (C#) สำหรับรันบนเครื่อง Windows Server เพื่อ Ping ปลุก IIS เป็นระยะ ป้องกันไม่ให้ Application Pool หลับ (Sleep / Idle Time-out)

---

## จุดเด่น
1. **Single File EXE**: คอมไพล์รวมทุกอย่างเป็นไฟล์ `.exe` ไฟล์เดียว นำไปวางบนเครื่อง Server แล้วดับเบิลคลิกได้เลย (ไม่ต้องลง .NET Runtime เพิ่มเติม)
2. **Configurable**: แก้ไข `appsettings.json` ได้โดยไม่ต้อง compile ใหม่
   - กำหนด URL ได้หลายตัว
   - กำหนดเวลารอบยิง (เช่น ทุก 30-60 นาที)
   - ข้าม SSL Error (สำหรับ Self-signed cert / localhost https)
3. **Daily Log**: มีระบบบันทึก Log ลงไฟล์อัตโนมัติในโฟลเดอร์ `logs/keepalive_YYYYMMDD.log`
4. **รองรับ Task Scheduler**: ปรับเป็น `RunOnce: true` เพื่อสั่งรันผ่าน Windows Task Scheduler ได้ หรือเปิดเป็น `RunOnce: false` เพื่อให้ทำงานเป็น Loop ในพื้นหลัง

---

## โครงสร้างโปรเจกต์
- `IisKeepAlive.csproj` - โปรเจกต์ .NET 8 พร้อมคำสั่ง Publish Single-File
- `Program.cs` - โค้ดหลัก
- `appsettings.json` - ไฟล์ตั้งค่า
- `.github/workflows/build.yml` - สคริปต์ GitHub Actions เพื่อสั่ง build `.exe` อัตโนมัติ

---

## วิธี Build ผ่าน GitHub Actions (อัตโนมัติ)
1. Push โปรเจกต์นี้ขึ้น GitHub Repository
2. ไปที่แท็บ **Actions** บน GitHub
3. เมื่อ Workflow `Build Windows EXE` ทำงานเสร็จ จะมี Artifact ให้ดาวน์โหลดชื่อ **`IisKeepAlive-win-x64`** ซึ่งมีไฟล์ `IisKeepAlive.exe` พร้อมใช้งานทันที

---

## วิธี Build ในเครื่องตนเอง (Manual)
รันคำสั่ง:
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./dist
```
จะได้ `IisKeepAlive.exe` อยู่ในโฟลเดอร์ `./dist`
