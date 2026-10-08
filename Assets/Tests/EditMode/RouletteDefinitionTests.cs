using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    public class RouletteDefinitionTests
    {
        private TestAssets assets;
        private RouletteDefinition definition;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        [Test]
        public void European_Has37PocketsWithNumbers0To36Once()
        {
            Assert.AreEqual(37, definition.PocketCount);

            var numbers = Enumerable.Range(0, definition.PocketCount).Select(i => definition.GetPocket(i).Number).ToList();
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 37), numbers);
            CollectionAssert.IsEmpty(definition.Validate());
        }

        [Test]
        public void European_FollowsSingleZeroWheelOrder()
        {
            int[] expected =
            {
                0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
                5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], definition.GetPocket(i).Number, $"칸 {i}");
                Assert.AreEqual(i, definition.IndexOfNumber(expected[i]));
            }
        }

        [Test]
        public void Zero_IsGreenAndAtIndex0()
        {
            Assert.AreEqual(0, definition.IndexOfNumber(0));
            Assert.AreEqual(PocketColor.Green, definition.GetPocket(0).Color);
        }

        [Test]
        public void Colors_Are18Red18Black_AndAlternateAroundWheel()
        {
            var red = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

            for (int i = 1; i < definition.PocketCount; i++)
            {
                PocketDefinition pocket = definition.GetPocket(i);
                Assert.AreEqual(red.Contains(pocket.Number) ? PocketColor.Red : PocketColor.Black, pocket.Color, $"번호 {pocket.Number}");

                // 0을 뺀 나머지는 빨강·검정이 번갈아 나온다.
                if (i > 1)
                {
                    Assert.AreNotEqual(definition.GetPocket(i - 1).Color, pocket.Color, $"칸 {i - 1}과 {i}");
                }
            }
        }

        [Test]
        public void BaseWeights_AreEqual()
        {
            for (int i = 0; i < definition.PocketCount; i++)
            {
                Assert.AreEqual(1, definition.GetPocket(i).BaseWeight);
            }
        }

        [Test]
        public void IndexOfNumber_UnknownNumber_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, definition.IndexOfNumber(37));
            Assert.AreEqual(-1, definition.IndexOfNumber(-1));
        }
    }
}
