using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 로봇 선반에 놓인 음료 한 잔(후보 하나). TableBetInput이 이 콜라이더를 광선으로 맞혀 호버·클릭을 알린다.
    /// 후보 번호는 GameFlowController.Rewards.CurrentOffer의 순서와 같다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ServedDrink : MonoBehaviour
    {
        public int OfferIndex { get; private set; }

        public DrinkOutline Outline { get; private set; }

        public void Configure(int offerIndex, DrinkOutline outline)
        {
            OfferIndex = offerIndex;
            Outline = outline;
        }
    }
}
