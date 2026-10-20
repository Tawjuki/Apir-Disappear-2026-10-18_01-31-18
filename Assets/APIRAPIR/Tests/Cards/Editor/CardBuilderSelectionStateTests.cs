using ApirApir.UI.CardBuilder;
using NUnit.Framework;
using UnityEngine;

namespace ApirApir.Cards.Tests
{
    public sealed class CardBuilderSelectionStateTests
    {
        [Test]
        public void SelectingAndReplacingOneSlotDoesNotChangeOtherSlots()
        {
            Texture2D texture = new Texture2D(2, 2);
            Sprite firstHead = CreateSprite(texture);
            Sprite replacementHead = CreateSprite(texture);
            Sprite body = CreateSprite(texture);
            var state = new CardBuilderSelectionState();

            try
            {
                state.Select(Option("head_a", CardBuilderPartSlot.Head, firstHead));
                state.Select(Option("body_a", CardBuilderPartSlot.Body, body));
                state.Select(Option("head_b", CardBuilderPartSlot.Head, replacementHead));

                Assert.That(state.Head.OptionId, Is.EqualTo("head_b"));
                Assert.That(state.Head.Sprite, Is.SameAs(replacementHead));
                Assert.That(state.Body.Sprite, Is.SameAs(body));
                Assert.That(state.Legs.IsEmpty, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(firstHead);
                Object.DestroyImmediate(replacementHead);
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void RemovingOneSlotPreservesTheOtherSelections()
        {
            Texture2D texture = new Texture2D(2, 2);
            Sprite head = CreateSprite(texture);
            Sprite body = CreateSprite(texture);
            Sprite legs = CreateSprite(texture);
            var state = new CardBuilderSelectionState();

            try
            {
                state.Select(Option("head", CardBuilderPartSlot.Head, head));
                state.Select(Option("body", CardBuilderPartSlot.Body, body));
                state.Select(Option("legs", CardBuilderPartSlot.Legs, legs));
                state.Remove(CardBuilderPartSlot.Body);

                Assert.That(state.Head.Sprite, Is.SameAs(head));
                Assert.That(state.Body.IsEmpty, Is.True);
                Assert.That(state.Legs.Sprite, Is.SameAs(legs));
                Assert.That(state.IsBlank, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(head);
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(legs);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ClearProducesAValidBlankCardState()
        {
            Texture2D texture = new Texture2D(2, 2);
            Sprite head = CreateSprite(texture);
            var state = new CardBuilderSelectionState();
            int changeCount = 0;
            state.Changed += () => changeCount++;

            try
            {
                state.Select(Option("head", CardBuilderPartSlot.Head, head));
                state.Clear();

                Assert.That(state.IsBlank, Is.True);
                Assert.That(state.Head.IsEmpty, Is.True);
                Assert.That(state.Body.IsEmpty, Is.True);
                Assert.That(state.Legs.IsEmpty, Is.True);
                Assert.That(changeCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(head);
                Object.DestroyImmediate(texture);
            }
        }

        private static CardBuilderPartOptionDefinition Option(
            string id,
            CardBuilderPartSlot slot,
            Sprite sprite)
        {
            return new CardBuilderPartOptionDefinition(id, id, slot, sprite);
        }

        private static Sprite CreateSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        }
    }
}
