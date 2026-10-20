# IIS Keep-Alive Tool

โปรแกรม .NET 8 (C#) สำหรับรันบนเครื่อง Windows Server เพื่อ Ping ปลุก IIS และ WCF Service เป็นระยะ ป้องกันไม่ให้ Application Pool หลับ (Sleep / Idle Time-out) และตัดปัญหา Cold Start ที่ทำให้ผู้ใช้งานคนแรกเจอกระตุกหรือหมุนค้าง

---

## จุดเด่น
1. **Single File EXE**: คอมไพล์รวมทุกอย่างเป็นไฟล์ `.exe` ไฟล์เดียว นำไปวางบนเครื่อง Server แล้วดับเบิลคลิกได้เลย (ไม่ต้องลง .NET Runtime เพิ่มเติม)
2. **รองรับ Direct WCF / API Method Warmup**:
   - รองรับกำหนด `Headers` (เช่น `Authorization`, `Token`, `Accept`, Custom Header)
   - รองรับกำหนด `Method` (GET, POST, PUT ฯลฯ)
   - รองรับส่ง `Body` JSON สำหรับยิง Method ที่ต้องการ Request Body
3. **ป้องกัน Cold Start ได้ตรงจุด**:
   - ขยาย Timeout เริ่มต้นเป็น 90 วินาที เพื่อรองรับจังหวะที่ WCF คอมไพล์และต่อ Database ครั้งแรก
   - รองรับการตั้ง `IntervalMinutes` ให้ต่ำกว่าค่า Idle Timeout ของ IIS (แนะนำ 10-15 นาที)
4. **Configurable**: แก้ไข `appsettings.json` ได้โดยไม่ต้อง compile ใหม่
5. **Daily Log**: มีระบบบันทึก Log ลงไฟล์อัตโนมัติในโฟลเดอร์ `logs/keepalive_YYYYMMDD.log`
6. **รองรับ Task Scheduler**: ปรับเป็น `RunOnce: true` เพื่อสั่งรันผ่าน Windows Task Scheduler ได้ หรือเปิดเป็น `RunOnce: false` เพื่อให้ทำงานเป็น Loop ในพื้นหลัง

---

## ตัวอย่างการตั้งค่า `appsettings.json`

### 1. แบบยิงเจาะจง Method (แนะนำ - ดึง Memory ขึ้น w3wp.exe ทันที)
```json
{
  "Settings": {
    "Targets": [
      {
        "Url": "http://127.0.0.1:8002/ProductRESTService.svc/GetSystemHospitalInfo",
        "Method": "GET",
        "Headers": {
          "Accept": "application/json",
          "Authorization": "Bearer YOUR_TOKEN_HERE"
        }
      }
    ],
    "IntervalMinutes": 15,
    "TimeoutSeconds": 90,
    "RunOnce": false,
    "IgnoreSslErrors": true,
    "LogToFile": true
  }
}
```

### 2. แบบยิง Method ที่ต้องส่ง POST + JSON Body
```json
{
  "Settings": {
    "Targets": [
      {
        "Url": "http://127.0.0.1:8002/ProductRESTService.svc/SearchHospital",
        "Method": "POST",
        "Headers": {
          "Accept": "application/json"
        },
        "ContentType": "application/json",
        "Body": "{\"HospitalCode\":\"1001\"}"
      }
    ],
    "IntervalMinutes": 15,
    "TimeoutSeconds": 90,
    "RunOnce": false,
    "IgnoreSslErrors": true,
    "LogToFile": true
  }
}
```

---

## ข้อควรระวังเรื่องการตั้งค่าเวลา (Interval)

> [!IMPORTANT]
> **ค่า Default Idle Time-out ของ IIS คือ 20 นาที**  
> หากตั้ง `IntervalMinutes` มากกว่า 20 นาที (เช่น 60 หรือ 120 นาที) ตัว IIS Application Pool จะหลับไปก่อนแล้ว ทำให้เกิด Cold Start ซ้ำอีก  
> **แนะนำให้ตั้ง `IntervalMinutes` อยู่ที่ 10 - 15 นาที** เพื่อให้ Worker Process (`w3wp.exe`) ตื่นและคงแคชข้อมูลไว้ตลอดเวลา

---

## การตรวจสอบผลลัพธ์
1. เปิดโปรแกรม `.exe` ขึ้นมา จะเห็น Log ทำงาน:
   ```text
   [PING] Requesting [GET]: http://127.0.0.1:8002/ProductRESTService.svc/GetSystemHospitalInfo
   [SUCCESS] Status: 200 OK | Time: 1250 ms
   ```
2. เปิด **Task Manager** -> แท็บ **Details** ดูโปรเซส `w3wp.exe`:
   - โปรเซส `w3wp.exe` จะถูกดึง Memory ขึ้นมาอยู่ที่ระดับ ~200 MB (เกิดจากการโหลด Assembly, WCF Service Contract, และ Entity/Database Connection Pool ขึ้น RAM เรียบร้อยแล้ว)
   - เมื่อผู้ใช้จริงเข้าใช้งานระบบ หน้าแรกจะโหลดได้ทันทีโดยไม่เจอกระตุก Cold Start

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
