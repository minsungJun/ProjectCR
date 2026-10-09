using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 음료를 서빙하는 로봇. 라운드를 통과하면 출발점(entryPoint)에서 정지점(servePoint)까지 굴러와 멈추고,
    /// 선반의 원(패드) 4곳에 후보 음료를 한 잔씩 올려 둔다. 고르면 고른 잔은 사라지고 나머지를 싣고 출발점으로 돌아간다.
    ///
    /// 이동은 시간 기준 보간(위치 Lerp, 회전 Slerp)이고 바퀴는 간 거리만큼 굴린다. 음료는 로봇의 자식이라 함께 움직인다.
    /// 잔은 모델 피벗과 상관없이 렌더러 범위의 바닥 가운데를 패드 윗면 가운데에 맞추고, 패드보다 크면 줄인다.
    /// 음료 모델이 없으면 강조색 원기둥으로 대신 놓는다. 후보·선택 상태는 GameFlowController가 가진다(여기는 그리기만).
    /// </summary>
    public class DrinkServerRobot : MonoBehaviour
    {
        [Tooltip("음료를 놓을 선반 원. 후보 순서대로(위 왼쪽, 위 오른쪽, 아래 왼쪽, 아래 오른쪽).")]
        [SerializeField] private Renderer[] pads = new Renderer[0];

        [Tooltip("이동할 때 굴릴 바퀴·허브. 로봇 좌우 축으로 돈다.")]
        [SerializeField] private Transform[] wheels = new Transform[0];

        [Tooltip("바퀴 반지름(m).")]
        [Min(0.005f)]
        [SerializeField] private float wheelRadius = 0.042f;

        [Tooltip("평소 대기 자리(화면 밖).")]
        [SerializeField] private Transform entryPoint;

        [Tooltip("서빙할 때 멈추는 자리(플레이어 오른쪽).")]
        [SerializeField] private Transform servePoint;

        [Tooltip("출발점 ↔ 정지점 이동 시간(초).")]
        [Min(0.1f)]
        [SerializeField] private float moveDuration = 1.8f;

        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("외곽선 머티리얼(ProjectCR/OutlineHull).")]
        [SerializeField] private Material outlineMaterial;

        [SerializeField] private Color outlineColor = new Color(1f, 0.84f, 0.42f, 1f);

        private readonly List<ServedDrink> drinks = new List<ServedDrink>();
        private int hovered = -1;
        private int selected = -1;

        public bool IsMoving { get; private set; }
        public bool IsServing { get; private set; }

        public void Configure(Renderer[] padRenderers, Transform[] wheelTransforms, Transform entry, Transform serve, Material outline)
        {
            pads = padRenderers;
            wheels = wheelTransforms;
            entryPoint = entry;
            servePoint = serve;
            outlineMaterial = outline;
        }

        private void Start()
        {
            // 평소에는 화면 밖 대기 자리.
            if (entryPoint != null && !IsServing && !IsMoving)
            {
                transform.SetPositionAndRotation(entryPoint.position, entryPoint.rotation);
            }
        }

        /// <summary>후보를 선반에 올리고 정지점까지 온다. 끝나면 IsServing.</summary>
        public IEnumerator Serve(IReadOnlyList<DrinkOffer> offers)
        {
            StopAllCoroutines();
            ClearDrinks();
            if (entryPoint != null)
            {
                transform.SetPositionAndRotation(entryPoint.position, entryPoint.rotation);
            }

            for (int i = 0; i < offers.Count && i < pads.Length; i++)
            {
                drinks.Add(PlaceDrink(i, offers[i]));
            }

            yield return MoveTo(servePoint);
            IsServing = true;
        }

        /// <summary>고른 잔을 치우고 나머지를 싣고 돌아간다.</summary>
        public IEnumerator Leave(int drunkIndex)
        {
            IsServing = false;
            SetHovered(-1);
            if (drunkIndex >= 0 && drunkIndex < drinks.Count && drinks[drunkIndex] != null)
            {
                drinks[drunkIndex].gameObject.SetActive(false);
            }

            foreach (ServedDrink drink in drinks)
            {
                if (drink != null)
                {
                    drink.Outline.SetState(false, false);
                }
            }

            yield return MoveTo(entryPoint);
            ClearDrinks();
        }

        /// <summary>새 게임 등: 바로 대기 자리로, 잔은 치운다.</summary>
        public void ResetToEntry()
        {
            StopAllCoroutines();
            IsMoving = false;
            IsServing = false;
            ClearDrinks();
            if (entryPoint != null)
            {
                transform.SetPositionAndRotation(entryPoint.position, entryPoint.rotation);
            }
        }

        public void SetHovered(int index)
        {
            hovered = index;
            RefreshOutlines();
        }

        public void SetSelected(int index)
        {
            selected = index;
            RefreshOutlines();
        }

        private void RefreshOutlines()
        {
            for (int i = 0; i < drinks.Count; i++)
            {
                if (drinks[i] != null)
                {
                    drinks[i].Outline.SetState(i == hovered, i == selected);
                }
            }
        }

        private IEnumerator MoveTo(Transform target)
        {
            if (target == null)
            {
                yield break;
            }

            IsMoving = true;
            Vector3 fromPosition = transform.position;
            Quaternion fromRotation = transform.rotation;
            Vector3 last = fromPosition;
            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = ease.Evaluate(Mathf.Clamp01(elapsed / moveDuration));
                transform.SetPositionAndRotation(Vector3.LerpUnclamped(fromPosition, target.position, t),
                    Quaternion.SlerpUnclamped(fromRotation, target.rotation, t));
                RollWheels(transform.position - last);
                last = transform.position;
                yield return null;
            }

            transform.SetPositionAndRotation(target.position, target.rotation);
            IsMoving = false;
        }

        /// <summary>간 거리만큼 바퀴를 굴린다(앞으로 가면 앞으로 구르게).</summary>
        private void RollWheels(Vector3 delta)
        {
            float forward = Vector3.Dot(delta, transform.forward);
            if (Mathf.Abs(forward) < 1e-6f)
            {
                return;
            }

            float degrees = forward / wheelRadius * Mathf.Rad2Deg;
            foreach (Transform wheel in wheels)
            {
                if (wheel != null)
                {
                    wheel.Rotate(transform.right, degrees, Space.World);
                }
            }
        }

        private ServedDrink PlaceDrink(int index, DrinkOffer offer)
        {
            Renderer pad = pads[index];
            GameObject model = offer.IsProbability ? offer.Probability.Model : offer.Payout.Model;

            var root = new GameObject($"ServedDrink_{index}_{offer.DisplayName}");
            root.transform.SetParent(transform, false);
            GameObject body = model != null ? Instantiate(model) : MakePlaceholder(offer.Tint);
            body.transform.SetParent(root.transform, true);
            foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
            {
                Destroy(c);
            }

            // 패드 지름보다 크면 줄이고, 렌더러 바닥 가운데를 패드 윗면 가운데에 맞춘다.
            Bounds bounds = BoundsOf(body);
            float padSize = Mathf.Min(pad.bounds.size.x, pad.bounds.size.z);
            float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            if (footprint > padSize * 1.15f)
            {
                body.transform.localScale *= padSize * 1.05f / footprint;
                bounds = BoundsOf(body);
            }

            Vector3 padTop = new Vector3(pad.bounds.center.x, pad.bounds.max.y, pad.bounds.center.z);
            body.transform.position += padTop - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = BoundsOf(body);

            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = root.transform.InverseTransformVector(bounds.size);

            var outline = root.AddComponent<DrinkOutline>();
            outline.Build(outlineMaterial, outlineColor);
            var served = root.AddComponent<ServedDrink>();
            served.Configure(index, outline);
            return served;
        }

        private static GameObject MakePlaceholder(Color tint)
        {
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            glass.name = "PlaceholderGlass";
            glass.transform.localScale = new Vector3(0.07f, 0.06f, 0.07f);
            var renderer = glass.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", tint);
            renderer.SetPropertyBlock(block);
            return glass;
        }

        private static Bounds BoundsOf(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.one * 0.05f);
            }

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            return bounds;
        }

        private void ClearDrinks()
        {
            foreach (ServedDrink drink in drinks)
            {
                if (drink != null)
                {
                    Destroy(drink.gameObject);
                }
            }

            drinks.Clear();
            hovered = -1;
            selected = -1;
        }
    }
}
