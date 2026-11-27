# Unity VR HW3 — Arduino / Meta Quest 雙向通訊

以 Arduino 可變電阻控制 VR 儀表指針，使用 VR 的 LED ON / LED OFF 按鈕控制實體 LED。沿用既有 Unity VR Template 專案，不重建場景，不使用 Python、Netcode 或 Horizon Link。

**與老師原始要求的差異：** 原題為 PC-based Unity VR 與 Arduino USB Full-duplex Serial Communication。因電腦 GPU 無法使用 Horizon Link / Air Link，本實作改由 Quest Android APK 執行 VR，Windows 只轉送 USB Serial ↔ TCP。Arduino 與 PC 仍為雙向 Serial，但 VR 與 Arduino 之間多了一層網路橋接，**不能宣稱完全符合原題**；應請老師確認替代架構是否接受。

```text
Arduino Uno (A0 可變電阻、D4 LED)
        ↕ USB Serial：COM5 / 9600 / 8N1
Windows C# Console TCP Bridge
        ↕ 同一可互通區域網路：TCP 8888
Meta Quest：Unity Android APK / OpenXR / XR Interaction Toolkit
```

## 檔案與實際工作位置

- 原始 Unity 專案實際位於 `C:\Unity\VR_HW3\VR_HW3`，此次已直接修改該處 Assets / ProjectSettings。
- 原始 Git checkout `/home/ccy/VR_HW3` 在本次工作區之外，未寫入。
- 可提交工作副本位於 `C:\Unity\VR_HW3\VR_HW3\Delivery`（WSL `/mnt/c/Unity/VR_HW3/VR_HW3/Delivery`）。它由原始 Repository clone，保留相同 Git 歷史與 GitHub origin；不是新的 GitHub Repository。
- Delivery 內的 `Unity/` 是既有專案的可提交副本，包含 Assets（含 .meta）、Packages、ProjectSettings。請勿同時編輯兩份專案；目前以原工作專案為開發來源，提交前同步必要檔案。
- Arduino 原始碼與通訊協定未修改。`Bridge/`、`Tests/` 不在 Unity 的 Assets 下，不會進入 APK。
- 美化依使用者後續指示略過；保留原外觀、材質、Transform、XR Origin。POT 與連線狀態可在 Inspector 查看。

```text
Delivery/
├── Arduino/VR_HW3/VR_HW3.ino
├── Bridge/                    Windows C# Console
├── Unity/{Assets,Packages,ProjectSettings}/
├── Tests/                     真實 TCP loopback 測試（無硬體）
├── Verification/              實際靜態檢查、工具失敗紀錄
├── Demo/
├── .gitignore
└── README.md
```

## 接線

| 元件 | 接線 |
| --- | --- |
| 可變電阻中間腳 | A0 |
| 可變電阻兩端 | 5V / GND |
| LED 正極 | D4 經限流電阻 |
| LED 負極 | GND |
| Arduino Uno USB | Windows PC，裝置管理員確認 COM5 |

Arduino 每 50 ms 以 millis() 非阻塞傳送 POT，原程式保留不覆寫。

## 通訊協定與行為

| 方向 | ASCII 封包 | 說明 |
| --- | --- | --- |
| Arduino → PC → Quest | `POT:512\n` | 範圍 0～1023，Arduino CRLF 也可接受 |
| Quest → PC → Arduino | `LED:1\n` | 開啟 LED |
| Quest → PC → Arduino | `LED:0\n` | 關閉 LED |

解析器跨讀取保留半包，一次讀取多行可逐行處理。超過 64 字元的一整行丟棄至下一個 LF；非法 POT、非法 LED 命令不轉送。沒有新增 ACK：Connected 只表示通訊連線，**不代表實體 LED 已確認亮起**。

Client 連線逾時 3 秒、重試間隔 2 秒，5 秒無合法 POT 便重連。背景執行緒只處理 I/O 與同步資料；主執行緒 Update 才更新公開 potValue / Inspector 狀態與 Debug.Log（最多每 5 秒一則狀態紀錄）。離線按鈕命令不補送；快速多次點擊只保留最新 LED 狀態。停止 Play / 關閉 APK 時停止背景工作並釋放連線。

Bridge 一次服務一個 Quest，Client 離線後接受下一個。Serial 寫入與 Socket 寫入設有逾時。Arduino USB 拔除造成 Serial 失敗時會停止 Bridge；接回後重新啟動 Bridge。Client 將自動重連。沒有 TLS / 身分驗證，僅限可信任 LAN。

## Windows Server 執行

在 **Windows PowerShell** 執行，不透過本次失效的 WSL interop。需要 .NET SDK 8 或更新版本，首次 restore 需可連 NuGet。目標 net8.0，已允許在較新的 .NET Runtime 執行；本機檔案顯示有 SDK 10.0.401，但此次未能啟動驗證。

1. 關閉 Arduino Serial Monitor、Serial Plotter，以及使用 WindowsSerial 模式的 Unity Play。COM5 只能有一個使用者。
2. `ipconfig` 找到 Windows Wi-Fi 或 Ethernet 的 IPv4，例如 `192.168.1.23`。不要使用 WSL vEthernet、127.0.0.1 或示例 IP 當作 Quest 的目的位址。
3. 在 PowerShell 執行（IP 改為實際值）：

```powershell
cd C:\Unity\VR_HW3\VR_HW3\Delivery
dotnet --list-sdks
dotnet run --project .\Bridge\ArduinoBridge.csproj -c Release -- COM5 9600 8888 192.168.1.23
# 或
.\Bridge\Run-Bridge.ps1 -ListenIP 192.168.1.23 -SerialPort COM5 -BaudRate 9600 -TcpPort 8888
```

應看到 `Serial opened`、`TCP listening`、約每兩秒一筆 `Serial RX POT:...`。連線後顯示 `Connected`，按按鈕後顯示 `Serial TX LED:1` / `LED:0`。Ctrl+C 停止。若不指定 Listen IP，程式預設只聽 127.0.0.1，供本機測試，Quest 無法連入。

可選的獨立執行檔：

```powershell
dotnet publish .\Bridge\ArduinoBridge.csproj -c Release -r win-x64 --self-contained true -o .\Bridge\bin\publish
.\Bridge\bin\publish\ArduinoBridge.exe COM5 9600 8888 192.168.1.23
```

## 防火牆與 Wi-Fi

PC 與 Quest 接上可互相通訊的 LAN；訪客 Wi-Fi、AP isolation、校園網路隔離可能阻擋同網段裝置。Windows 網路設為「私人網路」。如需新增規則，以系統管理員 PowerShell 執行：

```powershell
New-NetFirewallRule -DisplayName 'VR HW3 TCP 8888 LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8888 -Profile Private -RemoteAddress LocalSubnet
```

僅允許私人 LAN，不設定路由器 Port Forwarding。用另一台 LAN Windows 電腦執行 `Test-NetConnection 192.168.1.23 -Port 8888` 可測防火牆；同一台 PC 自測不能證明 Quest 可連通。IP 變更後需要更新 Inspector 並重建 APK；可在路由器設定 DHCP reservation。

## Unity Inspector 與 Android Build

1. Unity Hub 開啟 `C:\Unity\VR_HW3\VR_HW3`，使用 **6000.0.80f1**；等編譯完成。若 Editor 有未存檔的舊場景，不要把舊內容另存覆蓋本次修正；重新開啟 `Assets/Scenes/SampleScene.unity`。
2. Hierarchy 選 `ArduinoManager`：確認只有一份 ArduinoManager 元件。設定 **Mode = Tcp Client**、**Server IP = Windows 的 LAN IPv4**、**Server Port = 8888**。預填 `192.168.1.100` 只是佔位值，必須核對。
3. `Gauge > GaugeController`：Arduino Manager 指向上述元件，Needle Pivot 指向 Gauge/NeedlePivot。角度仍是 135 到 -135，Smooth Speed 保留原值 10.06。
4. `Gauge/LED_ON > XR Simple Interactable > Interactable Events > Select Entered` 應為 ArduinoManager.LEDOn；LED_OFF 應為 LEDOff。本次已檢查 YAML 並保留正確參照，不需重新綁定。
5. 儲存場景（Ctrl+S）。在 Windows Editor 直接 Serial 測試時，Mode 改 Windows Serial、Port Name COM5、Baud Rate 9600，先停 Bridge。Android player 強制使用 TCP，不編譯 Windows SerialPort 分支。
6. `File > Build Profiles > Android`，確認已切換 Android。Scene List 包含且勾選 `Assets/Scenes/SampleScene.unity`。沒有新增 Build Profile 資產；既有共用 Scene List 保留。
7. `Edit > Project Settings > XR Plug-in Management > Android`：OpenXR 與 Initialize XR on Startup；`OpenXR` 的 Android 頁籤確認 Meta Quest 支援與 Oculus Touch / Meta Quest Touch Plus Profile。既有套件維持 OpenXR 1.16.1、Meta OpenXR 2.5.0、XRI 3.4.1；未升級或移除套件。Project Validation 如有錯誤需依本機 Editor 訊息處理，不要直接升級套件。
8. `Project Settings > Player > Android > Other Settings`：Scripting Backend **IL2CPP**、Target Architectures **ARM64**、Internet Access **Require**。Minimum API 保留 32，Target API 保留 Automatic（此電腦 SDK 資料夾最高為 36）。
9. 使用新增選單 **HW3 > Build Quest APK**，會以共用 Scene List 輸出 `Builds/VR_HW3.apk`。也可使用一般 Build Profiles > Build。未重新設定私人簽章或識別碼。

網路權限由 `forceInternetPermission` 與建置 callback 確保產生 `<uses-permission android:name="android.permission.INTERNET" />`，不覆蓋 XR 的 Manifest。現有 API ≤36 的一般 LAN TCP 不需 Wi-Fi 掃描權限。若日後切到 target API 37+ 且裝置為 Android 17+，需要另外實作 `ACCESS_LOCAL_NETWORK` 執行時授權；本次未對該未部署環境加入權限流程。參考 [Unity Internet 權限](https://docs.unity3d.com/kr/6000.0/ScriptReference/PlayerSettings.Android-forceInternetPermission.html) 與 [Android LAN 權限版本說明](https://developer.android.com/privacy-and-security/local-network-permission)。

建置後可檢查合併 Manifest（路徑依 Unity 生成位置而異）：

```powershell
Get-ChildItem .\Library\Bee\Android -Recurse -Filter AndroidManifest.xml | Select-String 'android.permission.INTERNET'
```

## Quest 安裝與 Demo

1. 啟用 Quest 開發者模式，以 USB 接 PC，在頭盔允許 USB debugging。
2. 在 Windows PowerShell：

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.0.80f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adb devices -l
& $adb install -r 'C:\Unity\VR_HW3\VR_HW3\Builds\VR_HW3.apk'
& $adb logcat -s Unity
```

3. 啟動 Bridge，再從 Quest 應用程式庫的未知來源啟動 VR_HW3（顯示名稱沿用原專案）。VR 在頭盔執行，不需要 Horizon Link。USB 只用來安裝 / 除錯，可拔掉 Quest 的 USB 後測 Wi-Fi。
4. 旋轉可變電阻，觀察指針平滑掃過 135°～-135°；以控制器選取 LED ON / OFF，確認實體 LED 亮滅。狀態可透過 adb logcat 與 Bridge Console 查看；本次未新增 VR 文字 UI。
5. 停止 Bridge，Client 應變 Disconnected / Connecting；重新啟動 Bridge 後數值應恢復。離線時點擊 LED 不應在重連後突然補送。
6. Demo 錄影可同時拍到頭盔畫面、旋鈕、LED 與 Bridge 日誌，放在 Demo/，不包含大型 APK 或私人資料。

## 測試與目前結果

| 項目 | 本次結果 |
| --- | --- |
| 場景只有一個 ArduinoManager | 靜態檢查通過：移除多掛的 679693433，保留被事件引用的 679693434 |
| LED Select Entered / Gauge 參照 | 靜態檢查通過 |
| 所有原場景 Transform / Renderer / PrefabInstance | 與備份逐區塊比對一致 |
| 原腳本 .meta / 套件 manifest 與 lock | 位元組比對一致 |
| ARM64、IL2CPP、SampleScene 清單、Internet | 靜態檢查通過 |
| C# Bridge 編譯、協定 self-test、真實 Client loopback 測試 | 已建立測試，**未執行成功**：WSL 無 Linux .NET，Windows dotnet 啟動遇 interop 錯誤 |
| Unity C# 編譯、Android APK Build | **未驗證**：Unity.exe 在啟動階段遇相同錯誤，尚未進入編譯 |
| COM5 開啟、POT 實體輸入 | **待人工驗證** |
| TCP Client 實機連線、旋鈕平滑控制 | **待人工驗證** |
| LED ON / OFF 實體輸出 | **待人工驗證** |
| 斷線重連、停止 Play 釋放 COM5 | **待人工驗證** |
| Quest APK 執行與 Wi-Fi 控制 | **待人工驗證** |

實際工具錯誤：`WSL ERROR: UtilBindVsockAnyPort:307: socket failed 1`。本次 shell DNS 亦無法解析外部站點，未下載新的 SDK。這些限制不等於程式已通過編譯。

請先在 Windows 執行無硬體測試：

```powershell
cd C:\Unity\VR_HW3\VR_HW3\Delivery
.\Tests\Run-Checks.ps1
```

測試包含拆包、黏包、CRLF、超長行丟棄、非法命令，以及使用**實際 ArduinoManager 原始碼**配合最小 Unity API stub 的 loopback TCP：POT 接收、LED 雙向傳送、斷線重連、不補送離線命令、5 秒無資料重連與關閉時間。stub 測試不取代 Unity 編譯、XR 或硬體驗證。

## 故障排除

- **COM5 Access denied**：關閉 Serial Monitor / Plotter / Unity Serial 模式；確認只開一份 Bridge。場景重複元件已移除。
- **無 POT**：確認 Arduino 已上傳原程式、COM 號、9600、USB；先停 Bridge 再用 Serial Monitor 獨立測試。
- **Connection refused / timeout**：Bridge 是否啟動、是否綁正確 LAN IP、Inspector IP、8888、防火牆 Private / LocalSubnet、AP isolation、VPN。
- **每幾秒重連**：連線已建立但 5 秒沒有合法 POT；查看 Bridge 的 Serial RX 與 Arduino USB。
- **LED 沒反應**：Connected 不代表 ACK；先確認 Bridge 有 `Serial TX LED:x`，再查 D4、電阻、LED 極性。
- **找不到 System.IO.Ports（Editor）**：Windows Editor 的 API Compatibility Level 要使用 .NET Framework；現有專案已保留該設定。Android player 不引用此 API。
- **.NET restore 失敗**：在 Windows 測 NuGet 網路與 SDK；使用 Run-Checks.ps1 記錄編譯錯誤，不以靜態檢查代替編譯。
- **Quest 安裝 unauthorized**：戴上頭盔允許 USB debugging；`adb devices -l` 應顯示 device。
- **APK 舊 IP**：修改 Inspector 後儲存場景並重新 Build / install；不要只修改 Editor 的 Play Mode 值。

## Git 提交與推送

原始修改前備份保留於工作專案 `Verification/before-hw3.tgz`（不提交），Delivery 另有匯入原專案的 baseline commit。完整快取、Logs、UserSettings、bin/obj、APK 與憑證皆由遞迴 .gitignore 排除。Arduino 未修改。

```bash
cd /mnt/c/Unity/VR_HW3/VR_HW3/Delivery
git status --short
git diff --stat
git add -- Unity Bridge Tests Verification README.md .gitignore
git commit -m "feat: add Quest TCP client and Windows Arduino bridge"
git push origin main
```

若此次已建立 feature commit，無須重複 commit，直接 push 即可。若遠端已有新提交，先 fetch / 檢視差異並正常合併；不要 force push。後續可在原 `/home/ccy/VR_HW3` 用 `git pull --ff-only` 接收相同歷史。

本次遠端連線檢查失敗：`Could not resolve hostname github.com`；因此不能將本地 commit 視為已上傳 GitHub。恢復網路後請從 Delivery 執行 push。若先在原 Unity 工作專案修改 Inspector / 場景，可在 Windows PowerShell 同步：

```powershell
$source = 'C:\Unity\VR_HW3\VR_HW3'
Copy-Item "$source\Assets" "$source\Delivery\Unity" -Recurse -Force
Copy-Item "$source\Packages" "$source\Delivery\Unity" -Recurse -Force
Copy-Item "$source\ProjectSettings" "$source\Delivery\Unity" -Recurse -Force
```

此複製方式不會同步刪除檔案；若日後刪除 Unity 資產，也應在 Delivery 刪除對應檔案與 .meta，再檢查 Git diff。
