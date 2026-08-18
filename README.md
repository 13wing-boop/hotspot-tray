# HotspotTray

윈도우 11 모바일 핫스팟을 트레이 아이콘 하나로 켜고 끄는 가벼운 프로그램.

- **13.5 KB 단일 exe** — 설치 불필요, 외부 DLL·아이콘 파일 없음, .NET Framework 4.x(윈도우 기본 탑재)만 있으면 동작
- 윈도우 로그온 시 자동 실행 + 핫스팟 자동 켜기
- 트레이 아이콘 색으로 현재 상태 표시
- 좌클릭 = 즉시 On/Off 토글, 우클릭 = 메뉴
- **GitHub Releases 기반 자동 업데이트** — 새 버전이 올라오면 트레이에서 클릭 한 번으로 교체

## 설치 (사용할 PC에서)

PowerShell 창에 이 한 줄이면 끝납니다. 최신 릴리스를 받아 `%LOCALAPPDATA%\Programs\HotspotTray\`에 설치하고,
로그온 자동 실행까지 등록한 뒤 바로 실행합니다.

```powershell
irm https://raw.githubusercontent.com/13wing-boop/hotspot-tray/main/install.ps1 | iex
```

같은 명령을 다시 실행하면 그대로 최신 버전으로 갱신됩니다. 옵션이 필요하면 파일로 받아서:

```powershell
.\install.ps1 -NoAutoRun -NoStart -Dir "D:\Tools\HotspotTray"
```

### 수동 설치

1. [Releases](https://github.com/13wing-boop/hotspot-tray/releases/latest)에서 `HotspotTray.exe` 다운로드
2. **`%LOCALAPPDATA%\Programs\HotspotTray\` 폴더를 만들어 그 안에 두세요.**
   자동 업데이트가 자기 자신을 교체하려면 쓰기 권한이 필요합니다. `C:\Program Files` 아래에 두면 업데이트가 실패합니다.
3. 실행 → 트레이 아이콘 우클릭 → **"윈도우 시작 시 자동 실행"** 체크

브라우저로 직접 받으면 첫 실행 때 SmartScreen 경고가 뜹니다(코드 서명 인증서가 없어서 정상입니다).
`추가 정보` → `실행`을 누르면 되고, 이후에는 안 뜹니다.
`install.ps1`은 `Unblock-File`로 이 표시를 미리 지우므로 경고가 나오지 않습니다.

## 트레이 아이콘 상태

| 색 | 의미 | 툴팁 예시 |
|---|---|---|
| 🟢 초록 | 켜짐 | `핫스팟 켜짐 · 연결 2대 · JSH-SP 6054` |
| ⚪ 회색 | 꺼짐 | `핫스팟 꺼짐 · JSH-SP 6054` |
| 🟠 주황 | 전환 중 | `핫스팟 전환 중...` |
| 🔴 빨강 | 사용 불가 | `핫스팟 사용 불가 · 인터넷 연결 없음` |

## 우클릭 메뉴

- **핫스팟 켜기 / 끄기** — 현재 상태에 따라 바뀜
- **SSID · 비밀번호 복사** — 클립보드로 복사 (폰에 입력할 때 편함)
- **윈도우 시작 시 자동 실행** — 로그온 시 자동 실행 등록
- **시작할 때 핫스팟 자동 켜기** — 프로그램 시작과 동시에 핫스팟 On (기본값: 켜짐)
- **업데이트 확인 / 업데이트 설치 (vX.Y.Z)** — 새 버전이 있으면 문구가 바뀌고 굵게 표시됨
- **자동 업데이트 확인** — 시작 1분 뒤 + 24시간 주기로 확인 (기본값: 켜짐)
- **종료**

## 유지보수 · 패치 흐름

개발 PC에서 고치고 태그만 밀면, 사용 중인 PC는 트레이에서 클릭 한 번으로 최신이 됩니다.

```bash
git commit -am "핫스팟 상태 폴링 주기 조정"
git push
git tag v1.0.1
git push origin v1.0.1
```

태그를 밀면 GitHub Actions가 자동으로:

1. `src/HotspotTray.cs`의 버전 상수와 어셈블리 버전을 **태그 값으로 덮어씀** (exe 버전과 태그가 어긋날 일이 없음)
2. `build.cmd`로 exe 빌드
3. `v1.0.1` 릴리스를 만들고 `HotspotTray.exe`를 첨부

사용 중인 PC의 앱은 24시간 안에 스스로 알아채고 트레이 알림을 띄웁니다.
바로 받고 싶으면 우클릭 → **업데이트 확인**.

> 소스의 `Version` 상수는 태그 빌드에서 자동으로 맞춰지므로 커밋 때 손대지 않아도 됩니다.
> 다만 로컬 빌드본과 헷갈리지 않으려면 릴리스 후 한 번씩 맞춰두는 편이 좋습니다.

### 자동 업데이트 동작 방식

1. `https://api.github.com/repos/13wing-boop/hotspot-tray/releases/latest`에서 `tag_name`을 읽음
2. 현재 버전보다 높으면 트레이 알림 + 메뉴 문구 변경
3. 설치를 누르면 `HotspotTray.exe`를 `HotspotTray.exe.new`로 내려받음
4. 실행 중인 exe를 `HotspotTray.exe.old`로 **이름 변경**(윈도우는 실행 중인 exe의 리네임을 허용)
5. 새 파일을 제자리에 놓고 `/waitfor <pid>`로 재실행 → 이전 프로세스가 완전히 끝나길 기다렸다가(최대 15초) 뮤텍스 획득
6. 새 인스턴스가 기동하면서 남은 `.old` 파일을 삭제

교체 중 실패하면 `.old`를 되돌려 원래 버전을 유지합니다.
이 경로는 실제 릴리스 자산으로 전 구간 검증했습니다 — 실행 중 리네임, `/waitfor` 인계, `.old` 정리 모두 정상 동작합니다.

## 빌드 (개발 PC)

```bash
build.cmd
```

윈도우 내장 `csc.exe`(.NET Framework 4.8)와 `C:\Windows\System32\WinMetadata`의 WinRT 메타데이터만 사용합니다.
Visual Studio / .NET SDK / NuGet 전부 필요 없습니다. 결과물은 `bin\HotspotTray.exe`.

실행 인자:

- `/noauto` — 시작 시 핫스팟 자동 켜기를 건너뜀 (테스트용)
- `/waitfor <pid>` — 해당 프로세스 종료를 기다린 뒤 시작 (업데이트가 내부적으로 사용)

중복 실행은 뮤텍스로 차단됩니다.

## 설정 저장 위치

| 항목 | 위치 |
|---|---|
| 자동 실행 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `HotspotTray` |
| 시작 시 핫스팟 켜기 | `HKCU\Software\HotspotTray` → `StartHotspotOnLaunch` (DWORD, 기본 1) |
| 자동 업데이트 확인 | `HKCU\Software\HotspotTray` → `AutoUpdate` (DWORD, 기본 1) |

자동 실행은 **exe의 전체 경로**를 등록합니다. exe를 다른 폴더로 옮기면 메뉴에서 체크를 껐다 다시 켜주세요.

## 알아둘 점

- **로그온 시 실행이지 부팅 시 실행이 아닙니다.** 윈도우 테더링 API가 사용자 세션을 요구해서 SYSTEM 계정/부팅 트리거로는 동작하지 않습니다. 전원만 켜면 되게 하려면 `netplwiz`로 자동 로그인을 함께 설정하세요.
- **부팅 직후 재시도**: 인터넷 연결 프로필이 잡히기 전에는 핫스팟을 켤 수 없어서, 시작 시 5초 간격으로 최대 2분 30초간 재시도합니다.
- **연결된 기기가 없으면 윈도우가 약 5분 뒤 핫스팟을 자동으로 끕니다.** 설정 → 네트워크 및 인터넷 → 모바일 핫스팟에서 **"전원 절약"** 토글을 꺼두세요. 윈도우 기본 동작이라 프로그램에서 바꿀 수 없습니다.
- SSID/비밀번호 변경은 윈도우 설정 앱에서 하세요. 이 프로그램은 읽기만 합니다.

## 구조

```
src/HotspotTray.cs           전체 소스 (단일 파일)
build.cmd                    빌드 스크립트
install.ps1                  다른 PC용 설치/갱신 스크립트
.github/workflows/build.yml  CI + 태그 푸시 시 릴리스 자동 생성
bin/HotspotTray.exe          빌드 결과물 (git 추적 안 함)
```

핵심 API는 `Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager`입니다.
Windows SDK의 통합 `Windows.winmd`가 없으면 `System.Runtime.WindowsRuntime`의 `await` 확장 메서드가
분할 winmd와 타입 식별자가 맞지 않으므로, `IAsyncOperation.Completed` 콜백을 백그라운드 스레드에서
직접 대기하는 방식(`WaitOp<T>`)을 씁니다. UI 갱신은 2초 주기 WinForms 타이머가 담당합니다.

## 라이선스

MIT
