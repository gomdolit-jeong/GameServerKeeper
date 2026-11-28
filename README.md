# GameServerKeeper (C# .NET 8 WPF · MVVM)

여러 데디케이트 서버(게임 종류 무관)를 한 프로그램에서 운영하는 매니저.
현재 지원: **Palworld** (REST API 연동), **V Rising** (RCON 공지), **Core Keeper**, **범용** (전용 연동 없는 아무 서버).

## 빌드 / 배포
```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
실행 파일 옆에 `config.json`, `logs\yyyy-MM-dd.log` 가 생성됩니다. (로그는 `[서버이름/소스]` 로 구분)

## 구조
```
GameServerKeeper/
├─ App.xaml(.cs)                 시작점: Core → Services → ViewModel → View 조립, 중복 실행 방지
├─ Core/                         ← 게임 중립 (새 게임을 추가해도 수정 불필요, UI 를 모름)
│  ├─ Logging/Logger.cs          UI + 일별 파일 로그, 서버별 스코프 로거
│  ├─ Config/
│  │  ├─ AppConfig.cs            전역 설정 + 서버 목록 + 서버 간 충돌 검사
│  │  ├─ ServerProfile.cs        서버 1대 설정 (공통 항목 + Game 전용 설정)
│  │  └─ ConfigManager.cs        config.json (GameSettings 다형성 직렬화)
│  ├─ Games/
│  │  ├─ IGameServerAdapter.cs   게임 어댑터 인터페이스 + 기능 플래그(GameCapabilities)
│  │  └─ GameRegistry.cs         지원 게임 목록  ← 새 게임 등록 위치
│  ├─ Services/
│  │  ├─ ProcessManager.cs       실행 → 자손 중 실제 서버 프로세스 추적/메모리/Kill
│  │  ├─ SteamCmdUpdater.cs      app_update (steamcmd 경로별 직렬화)
│  │  ├─ BackupManager.cs        세이브 → {접두어}_yyyyMMdd_HHmmss.zip, 보관 정리
│  │  ├─ ConsoleCtrl.cs          콘솔 서버에 Ctrl+C 전송 (범용 서버 정상 종료)
│  │  └─ DiscordNotifier.cs
│  └─ Server/
│     ├─ ServerInstance.cs       서버 1대 오케스트레이션 (안전 종료/크래시/메모리/정기 재시작)
│     └─ ServerManager.cs        서버 추가/편집/삭제, 전체 종료
├─ Games/
│  ├─ Palworld/                  PalworldSettings, PalworldAdapter, PalworldRestClient
│  ├─ VRising/                   VRisingSettings(RCON), VRisingAdapter
│  ├─ CoreKeeper/                CoreKeeperSettings, CoreKeeperAdapter
│  └─ Generic/                   GenericSettings, GenericAdapter
├─ ViewModels/                   ← 화면 상태와 명령 (WPF 를 모름, CommunityToolkit.Mvvm)
│  ├─ MainViewModel.cs           사이드바·페이지 전환, 로그 분배, 서버 추가/편집/삭제, 종료 확인
│  ├─ OverviewViewModel.cs       전체 현황 요약 타일 + 서버 카드 목록
│  ├─ ServerViewModel.cs         서버 1대 (상세 화면·카드·사이드바 공용): 상태 반영 + 시작/종료/백업 명령
│  ├─ SettingsViewModel.cs       설정 화면 (설정 클래스의 특성을 읽어 항목 ViewModel 자동 생성)
│  ├─ DialogViewModels.cs        서버 추가(실행 파일로 게임 자동 인식), 입력, 선택 대화상자
│  ├─ LogViewModel.cs            로그 창 (줄 수 제한, 줄바꿈, 복사, 지우기)
│  └─ Common.cs                  Tone(의미 색), StatInfo, 아이콘 글리프, 표시 형식
├─ Services/                     ← ViewModel 이 창을 직접 만들지 않도록 하는 경계
│  ├─ IDialogService.cs          대화상자·파일 선택 인터페이스 (+ IShellService)
│  ├─ DialogService.cs           WPF 구현
│  ├─ ShellService.cs            탐색기 열기, 클립보드
│  └─ TrayService.cs             트레이 아이콘·메뉴 (WinForms NotifyIcon 은 여기서만 사용)
├─ Views/                        ← XAML (코드 비하인드는 창 동작·스크롤 같은 순수 화면 동작만)
│  ├─ MainWindow / OverviewView / ServerView
│  ├─ SettingsWindow / AddServerWindow / PromptWindow / ChoiceWindow
│  ├─ Controls/                  StatTile, StatusPill, LogView
│  └─ Converters.cs / Attached.cs / AppIcon.cs
└─ Themes/Theme.xaml             색상·글꼴·버튼·스위치·입력·스크롤바·툴팁 스타일
```

화면 색은 ViewModel 이 정하지 않고 `Tone`(Success/Warning/...)만 넘기며, 실제 색은 `Views/Converters.cs` 와 `Themes/Theme.xaml` 에서 정합니다.

## 서버 추가 흐름
1. **서버 추가** → 서버 실행 파일과 이름만 입력
2. 실행 파일 이름으로 게임을 **자동 인식** (예: `PalServer.exe` → Palworld). 모르는 파일이면 **범용**
3. 인식한 게임의 기본값(App ID, 세이브·백업 폴더 등)이 채워진 설정 화면에서 확인 후 저장
4. 인식이 틀렸으면 설정 화면의 **게임 종류**에서 변경 (서버가 꺼져 있을 때만 가능)

## 새 게임 추가 방법 (예: V Rising, Core Keeper)
1. `Games/{게임}/` 에 `GameSettings` 파생 클래스 작성 — 그 게임만의 설정 (RCON 포트/비밀번호 등)
   - 속성에 `DisplayName` / `Description` / `Range` / `PathEditor` 특성을 달면 **설정 화면·툴팁이 자동으로 생성**됩니다.
2. `GameServerAdapterBase` 파생 어댑터 작성 — 지원하는 것만 override
   - `Capabilities` : PlayerList / Announce / Save / GracefulShutdown 중 지원하는 것
   - `TrackedProcessName` : 런처가 자식 프로세스를 띄우면 그 이름
   - `GetPlayersAsync`, `AnnounceAsync`, `SaveAsync`, `RequestShutdownAsync`, `CheckConfiguration`
3. `GameDefinition` 작성 후 `GameRegistry` 목록에 한 줄 추가 (범용보다 앞에)
   - `ExeFileNames` : 자동 인식할 실행 파일 이름 (예: `["VRisingServer.exe"]`)
   - `CreateProfile(exePath)` : 고른 실행 파일 위치 기준으로 설치·세이브·백업 폴더 등 기본값 채우기
   - `DefaultAppId`, `Description`(서버 추가 화면 설명), `SettingsNote`(설정 화면 안내)

지원하지 않는 기능은 공통 로직과 설정 화면이 자동으로 건너뜁니다 (관련 설정 항목도 숨김).
(예: 접속자 조회 불가 → 메모리 재시작 시 0명 판단 불가하므로 공지 기능이 있으면 항상 카운트다운)
전용 어댑터를 만들기 전에도 **범용** 타입으로 등록하면 프로세스 감시·크래시 복구·메모리 재시작·백업·업데이트·정기 재시작은 바로 쓸 수 있습니다.

## 여러 서버 운영 시 주의
- 서버마다 **설치 폴더 / 세이브 폴더가 달라야** 합니다 (저장 시 검사).
- 같은 백업 폴더를 공유해도 되지만 **백업 접두어는 달라야** 합니다 (보관 정리가 서로의 백업을 지우지 않도록).
- 게임 포트·API 포트는 서버마다 다르게 지정하세요.
- 같은 SteamCMD 를 쓰는 업데이트는 자동으로 한 대씩 순서대로 실행됩니다. 정기 재시작 시각을 엇갈리게 두면 더 좋습니다.

## 팰월드 서버 필수 설정 (`Pal\Saved\Config\WindowsServer\PalWorldSettings.ini`)
```
RESTAPIEnabled=True,RESTAPIPort=8212,AdminPassword="원하는비밀번호"
```
매니저의 서버 설정 → 게임 설정의 포트/AdminPassword 와 같아야 합니다 (시작 시 자동 점검).
REST API 포트는 방화벽에서 외부 접근을 막으세요.

## 동작 요약
| 기능 | 동작 |
|---|---|
| 안전 종료 | (접속자 있으면) 카운트다운 공지 → 저장 → 종료 요청 → 대기 → 미종료 시에만 Kill. 게임이 지원하지 않는 단계는 생략 |
| 크래시 복구 | 요청하지 않은 종료 → 로그/디스코드 → 지연 후 재시작. 시간 창 내 허용 횟수 초과 시 중단 |
| 메모리 | 주기적으로 실제 서버 프로세스 WorkingSet64 측정, 임계치 초과 시 안전 재시작 |
| 정기 재시작 | 서버별 지정 시각~10분 이내 하루 1회: 안전 종료 → 백업 → 업데이트 → 시작 |
| 매니저 재실행 | 설치 폴더 기준으로 실행 중인 서버를 찾아 자동 연결 |
| 매니저 종료 | 모든 서버 병렬 안전 종료 후 종료 / 서버는 둔 채 매니저만 종료 |
