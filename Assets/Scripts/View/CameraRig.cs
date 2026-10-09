using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 연출 카메라를 정해진 자리(빈 오브젝트의 Transform)로 부드럽게 옮긴다.
    /// 베팅할 때는 베팅판 자리, 회전할 때는 룰렛 자리. 자리 오브젝트를 씬에서 옮기면 그대로 따른다.
    ///
    /// 이동은 경과 시간 기준 보간(위치 Lerp, 회전 Slerp)이라 프레임 속도와 상관없이 같은 시간에 도착한다.
    /// 새 이동을 시작하면 하던 이동은 지금 자리에서 이어서 꺾는다.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        [Tooltip("움직일 카메라. 비우면 이 오브젝트.")]
        [SerializeField] private Transform cameraTransform;

        [Tooltip("베팅할 때 카메라 자리.")]
        [SerializeField] private Transform bettingPose;

        [Tooltip("회전할 때 카메라 자리. 여기 도착한 뒤 회전이 시작된다.")]
        [SerializeField] private Transform roulettePose;

        [Tooltip("음료 고를 때 카메라 자리(오른쪽 서빙 로봇을 보는 자리).")]
        [SerializeField] private Transform drinkPose;

        [Tooltip("게임 시작 자리(좌석에서 홀을 보는 시점). 튜토리얼 대화도 여기서 시작한다. 비우면 베팅판 자리.")]
        [SerializeField] private Transform defaultPose;

        [Tooltip("자리 사이를 옮겨 가는 시간(초).")]
        [Min(0.05f)]
        [SerializeField] private float moveDuration = 0.9f;

        [Tooltip("이동 진행도(0→1). 기본은 천천히 출발해 천천히 멈춘다.")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("결과가 나온 뒤 베팅판 자리로 돌아가기 전에 룰렛을 보여 주는 시간(초).")]
        [Min(0f)]
        [SerializeField] private float returnDelay = 1.5f;

        private Transform target;
        private Vector3 fromPosition;
        private Quaternion fromRotation;
        private float elapsed;
        private float waitLeft;

        /// <summary>이동 중이거나 이동을 기다리는 중.</summary>
        public bool IsMoving => target != null;

        public float ReturnDelay => returnDelay;

        private Transform MovedCamera => cameraTransform != null ? cameraTransform : transform;

        public void SnapToBetting() => Snap(bettingPose);

        public void MoveToBetting(float delay = 0f) => MoveTo(bettingPose, delay);

        public void MoveToRoulette() => MoveTo(roulettePose, 0f);

        public void MoveToDrink() => MoveTo(drinkPose, 0f);

        public void SnapToDefault() => Snap(defaultPose != null ? defaultPose : bettingPose);

        public void MoveToDefault() => MoveTo(defaultPose != null ? defaultPose : bettingPose, 0f);

        private void Snap(Transform pose)
        {
            target = null;
            if (pose != null)
            {
                MovedCamera.SetPositionAndRotation(pose.position, pose.rotation);
            }
        }

        private void MoveTo(Transform pose, float delay)
        {
            if (pose == null)
            {
                target = null;
                return;
            }

            target = pose;
            waitLeft = delay;
            BeginLeg();
        }

        private void BeginLeg()
        {
            fromPosition = MovedCamera.position;
            fromRotation = MovedCamera.rotation;
            elapsed = 0f;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            if (waitLeft > 0f)
            {
                waitLeft -= Time.deltaTime;
                if (waitLeft > 0f)
                {
                    return;
                }

                // 기다리는 동안 카메라가 바뀌었을 수 있으니 출발점을 지금 자리로 다시 잡는다.
                BeginLeg();
            }

            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / moveDuration);
            float t = ease.Evaluate(u);
            MovedCamera.SetPositionAndRotation(
                Vector3.LerpUnclamped(fromPosition, target.position, t),
                Quaternion.SlerpUnclamped(fromRotation, target.rotation, t));

            if (u >= 1f)
            {
                MovedCamera.SetPositionAndRotation(target.position, target.rotation);
                target = null;
            }
        }
    }
}
