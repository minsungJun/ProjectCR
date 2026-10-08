---
tags: [ProjectCR, 설계]
created: 2026-10-08
---

# ProjectCR 설계

3D 룰렛 로그라이크. 라운드마다 정해진 회전 수 안에 돈을 불려 납부금을 내야 다음 라운드로 간다.
지금은 38칸 해골 휠(0~36 + 해골), 숫자 하나·바깥 베팅(회전당 하나), 라운드·납부까지 있다. 아이템은 아직 없다.

## 핵심 원칙

- **결과는 로직이 먼저 정하고, 3D는 그 칸에 멈추는 연출만 한다.** 물리로 판정하지 않는다.
  정산은 공의 실제 위치가 아니라 `SpinResult`를 본다.
- 결과는 처음부터 공 여러 개를 담는다(`SpinResult.Balls`). 기본 플레이는 공 1개.
- 정적 설정(ScriptableObject)은 런타임에 고치지 않는다. 회전마다 `SpinConfig`로 복사하고 아이템은 복사본만 고친다.
- 중복 회전·중복 차감·중복 정산은 UI가 아니라 `GameFlowController`가 막는다.

## 구조

| 파일 | 역할 |
|---|---|
| `Roulette/RouletteDefinition` (SO) | 칸 배열. **배열 인덱스 = 휠 위 순서**(위에서 볼 때 시계 방향, 0번이 인덱스 0). 칸 종류(번호/해골)·번호·색·기본 가중치. 에셋은 `Data/RouletteDefinition_Skull38.asset` |
| `Flow/GameBalance` (SO) | 밸런스 임시값 전부: 초기 자금, 최소 베팅, 지급 배수, 공 수, 라운드 회전 수, 납부금 공식 |
| `Roulette/SpinConfig` | 회전 1회분 복사본: 공 수, 칸별 가중치, 배수 묶음별 지급 배수(`PayoutFor`) |
| `Roulette/SpinResolver` | 공별 독립 가중치 추첨, 디버그 지정 번호 덮어쓰기 |
| `Economy/Wallet` | 돈(long). 상한 999,999,999,999에서 포화 |
| `Economy/BetTarget` | 무엇에 걸었나(숫자 하나 / 바깥 베팅 12종). **어느 칸이 포함되는지는 `BetTarget.Covers` 한곳에서 정한다** |
| `Economy/BetManager` | 베팅 대상·금액 선택, 검증(`BetError`), 확정 시 1회 차감 후 잠금 |
| `Economy/PayoutCalculator` | 회전 전체 평가(해골 먼저) → 공별 판정·지급 합산. 결과 이유 `SpinOutcome`. `checked` 곱셈, 넘치면 상한 |
| `Flow/RoundManager` | 라운드, 남은 회전, 납부 |
| `Flow/GameFlowController` | 상태 전환 `Betting → Spinning → (Betting | RoundPayment) → GameOver`. 회전 기록(`SpinsPlayed`, `ConsecutiveLosses`). 순수 C#라 EditMode 테스트 |
| `Flow/RouletteGame` (MB) | 씬 진입점. HUD ↔ Flow ↔ Presenter 연결, 디버그 옵션, 전체 칸 순회 검증 |
| `View/RoulettePresenter` (MB) | 휠 회전, 공 궤적, 안착, 당첨 칸 강조 |
| `View/BallTrajectory` | 휠 로컬 상대 각도 계산(아래) |
| `View/SpinMotionSettings` (SO) | 단계 시간, AnimationCurve(각도·반경·높이·바운스), 휠 속도 |
| `View/PocketAnchor` (MB) | 칸 인덱스·번호를 명시한 안착점 |
| `UI/RouletteHud` (MB) + `Assets/UI/RouletteHud.uxml/uss` | UI Toolkit HUD |
| `Editor/RouletteSetup` | 메뉴 `Tools/ProjectCR/Setup All`: 데이터 에셋, 임시 모델 프리팹, 씬 생성 |

한 회전의 순서와 아이템이 끼어들 자리:

```
베팅 검증 → 확정·차감 → BuildSpinConfig()   ← 회전 전: 공 수, 칸 가중치, 지급 배율
                     → SpinResolver.Resolve
                     → ApplyResultOverrides() ← 결과 확정 후: 재추첨, 결과 변환 (지금은 디버그 지정 번호)
                     → presenter.Play(result) ← 모든 공의 연출이 끝날 때까지. 해골이어도 중단하지 않는다
                     → CompleteSpin(id)        ← 전체 결과 평가(해골 먼저) → 정산(배율·보너스) → 기록
                     → 다음 진행(Betting 또는 RoundPayment)
                     → SubmitPayment()         ← 라운드 종료: 납부금 할인, 추가 보상
```

## 해골 칸

38칸 = 0~36 + 해골. 해골은 인덱스 19, 0의 정반대(10과 5 사이)다. 콘셉트 시안과 같은 배치.

- 데이터: `PocketKind.Skull`, 번호 `RouletteDefinition.SkullNumber`(-2), 색 `PocketColor.None`.
  번호 조회(`IndexOfNumber`/`ContainsNumber`)에서 빠지므로 **베팅할 수 없고**, 색이 없어서 색 베팅에도 걸리지 않는다.
  번호(-2)로 홀짝 같은 계산을 하기 전에 반드시 `IsSkull`부터 본다(`PayoutCalculator.Covers`).
- **공 하나라도 해골이면 그 회전의 모든 베팅이 낙첨**이다. 다른 공이 맞혀도 지급 0. 시작 때 뺀 베팅금은 돌려주지 않는다.
  결과 이유는 `SpinOutcome.SkullBust`로 일반 낙첨(`Lose`)과 구분한다.
- 해골은 **상태를 바꾸지 않는다.** 정상 회전 종료라 회전 1회를 쓰고 일반 회전과 같이 Betting/RoundPayment로 간다.
  GameOver는 납부 실패에서만 나온다. 마지막 회전 해골도 그대로 납부 단계로 간다.
- 공이 여러 개면 모든 공의 연출이 끝난 뒤 한 번만 평가·정산한다. 공별로 먼저 지급하는 경로는 없다.
- 해골 판정이 정산 맨 앞이라 지급 배율·당첨 보너스·낙첨 환급은 해골 회전에 적용되지 않는다.
  "해골에도 환급" 같은 효과는 나중에 SkullBust 전용 단계로 따로 붙인다. 해골을 없애는 재추첨·결과 변환은 요구가 생기기 전까지 넣지 않는다.
- 연속 낙첨 기록(`ConsecutiveLosses`)에는 해골 낙첨도 들어간다.
- UI: 해골 베팅 칸은 없다. 번호판 아래 고정 문구 "해골: 이번 회전의 모든 베팅 낙첨".
  결과는 일반 결과 패널에 `결과 ☠ 해골 — 해골 낙첨 · 지급 0 (−베팅액)`, 최근 기록에 ☠. 패배 화면·재시작·즉사 연출과는 연결하지 않는다.
- 공 1개 기준 해골 확률 1/38. 숫자 베팅 36배를 그대로 두면 기대 환수율이 97.3% → 94.7%로 내려간다.

## 공이 목표 칸에 들어가는 원리

공 위치를 **WheelPivot 로컬 좌표(휠에 대한 상대 각도 φ)** 로 계산한다.

```
u    = clamp01(elapsed / duration)
φ(u) = φ0 + Δ·angleCurve(u),   Δ = −(360·N + ((φ0 − 목표각) mod 360))
```

φ(1)이 목표 칸 각도와 360° 단위로 같으므로 휠 속도·프레임 속도와 무관하게 착지한다.
u ≥ 1이면 커브 값과 상관없이 정확히 끝 각도를 쓴다. 착지하면 공을 PocketAnchor 자식으로 붙인다.
세계 기준 공 속도 = 휠 속도 + φ' 이라 초반엔 휠 반대로 돌고, φ'이 0이 되면 휠에 실려 멈춘다.

각도 규약: 위에서 볼 때 시계 방향 +, 로컬 +Z가 0° (`Quaternion.Euler(0, a, 0) * forward`).

## 임시 모델과 교체 규약

모델은 아직 없다. `RouletteSetup`이 기본 도형과 절차적 메시(부채꼴 칸, 회전체 그릇)로 만든다.
번호 라벨은 TMP 리소스를 가져오지 않으려고 내장 `TextMesh`를 쓴다(아트 단계에서 교체).

진짜 모델로 바꿀 때 지킬 것:

1. 피벗은 휠 중심, Y 위.
2. 0번 칸 중심이 로컬 +Z, 위에서 볼 때 시계 방향 해골 휠 순서(`RouletteDefinition.SkullWheelOrder`, 해골은 0의 정반대). 다르면 앵커 배치로 맞춘다.
3. 칸별 Renderer(당첨 강조용, URP Lit `_BaseColor`).
4. 안착 반경·높이는 앵커 위치에서 읽으므로 모델에 맞춰 앵커만 옮기면 된다. 볼 트랙 반경·높이는 `RoulettePresenter`의 `rimRadius/rimHeight`.
5. 교체 후 플레이 모드에서 `RouletteGame` 컨텍스트 메뉴 **전체 칸 순회 검증**을 돌린다.

해골 라벨은 지금 TextMesh의 ☠ 글자(OS 대체 폰트로 보인다)다. 아트 단계에서 아이콘으로 바꾼다.

## 규칙(임시값)

- 베팅은 **회전당 하나**: 숫자 하나 또는 바깥 베팅 하나. 여러 베팅을 동시에 거는 건 아직 없다.
- 원금 포함 총 지급 배수(`GameBalance`, 회전마다 `SpinConfig`로 복사):

  | 묶음(`PayoutClass`) | 베팅 | 칸 수 | 배수 |
  |---|---|---|---|
  | Straight | 숫자 하나(0 포함) | 1 | 36 |
  | DozenColumn | 1–12 / 13–24 / 25–36, 1열(1·4·…·34) / 2열 / 3열(3·6·…·36) | 12 | 3 |
  | EvenMoney | 빨강·검정, 홀·짝, 1–18·19–36 | 18 | 2 |

  배수 × 칸 수 = 36이라 38칸 휠에서는 어떤 베팅이든 기대 환수율이 36/38(94.7%)로 같다.
- **0이 나오면 바깥 베팅은 모두 진다**(표준 규칙. 절반 환급 같은 라 파르타주 규칙은 쓰지 않는다). 해골은 어떤 베팅에도 포함되지 않는다.
- 낙첨 지급 0.
- 공 여러 개: 공별 독립 추첨, 차감은 회전당 1회, 공별 지급 합산. 같은 칸이면 칸 안에서 안쪽·바깥쪽으로 비켜 놓고, 공마다 0.35초씩 늦게 들어온다.
- 초기 100, 최소 베팅 1, 라운드당 5회전, 납부금 120 → 180 → 270 → 410 (×1.5, 10단위 올림).
- 회전이 남았어도 최소 베팅을 못 내면 남은 회전을 버리고 납부 판정으로 간다. 바로 게임 오버로 보내지 않는 건 납부금 할인 같은 효과가 생길 수 있어서다.
- 숫자 베팅만 있으면 1라운드 통과는 5번 안에 한 번 맞힐 확률(≈13%)에 달려 있다. 밸런스는 아이템이 들어온 뒤 잡는다.

## 검증

- 자동: `Assets/Tests/EditMode` 77개 — 칸 배열(37·38칸), 추첨 재현성·분포·가중치 0, 정산·상한, 베팅 오류, 중복 회전·정산, 라운드·납부·이월, 착지 각도(프레임 간격·끊김), 해골 규칙(`SkullRuleTests`), 바깥 베팅 포함 칸·배수·환수율(`OutsideBetTests`).
- 플레이 모드: `RouletteGame` 컨텍스트 메뉴
  - **전체 칸 순회 검증** — 2026-10-08 38칸(해골 포함) targetFrameRate 20 / 60 / 제한 없음 × 배속 8에서 불일치 0.
  - **같은 칸에 공 2개 연출**
- 디버그 필드(`RouletteGame` 인스펙터): `randomSeed`(0=무작위, 실제 시드는 콘솔), `forcedNumbers`(-1 추첨대로, -2 해골), `extraBallCount`, `straightPayoutOverride`(숫자 베팅 배수만), `presentationSpeed`.
- HUD 오른쪽 위 **시점 전환**: 연출 시점 ↔ 탑다운 정사영.

## 남은 일

- 안쪽 베팅(스플릿·스트리트·코너·식스 라인·퍼스트 포): `BetKind`와 `Covers`에 추가하고 번호판에 경계 클릭 영역을 둔다
- 한 회전에 여러 베팅(베팅 목록 + 칩 배치 UI). 해골 판정은 이미 회전 단위라 그대로 쓴다
- 아이템 시스템 (위 끼어들 자리에 연결)
- 아트 모델, TMP 라벨, 효과음(`RoulettePresenter.CurrentStage`)
