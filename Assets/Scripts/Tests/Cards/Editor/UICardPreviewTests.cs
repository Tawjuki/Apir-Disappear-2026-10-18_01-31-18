using ApirApir.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.Cards.Tests
{
    public sealed class UICardPreviewTests
    {
        [Test]
        public void EmptyPartsKeepBackgroundVisibleAndBehindAllSlots()
        {
            PreviewFixture fixture = new PreviewFixture();
            try
            {
                fixture.Preview.ClearParts();

                Assert.That(fixture.Background.enabled, Is.True);
                Assert.That(fixture.Legs.enabled, Is.False);
                Assert.That(fixture.Body.enabled, Is.False);
                Assert.That(fixture.Head.enabled, Is.False);
                Assert.That(fixture.Background.transform.GetSiblingIndex(),
                    Is.LessThan(fixture.Legs.transform.GetSiblingIndex()));
                Assert.That(fixture.Legs.transform.GetSiblingIndex(),
                    Is.LessThan(fixture.Body.transform.GetSiblingIndex()));
                Assert.That(fixture.Body.transform.GetSiblingIndex(),
                    Is.LessThan(fixture.Head.transform.GetSiblingIndex()));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void PartAssignmentsReplaceIndependentlyWithoutRecreatingSlots()
        {
            PreviewFixture fixture = new PreviewFixture();
            Sprite firstHead = fixture.CreateSprite();
            Sprite replacementHead = fixture.CreateSprite();
            Sprite body = fixture.CreateSprite();
            Sprite legs = fixture.CreateSprite();
            int headObjectId = fixture.Head.gameObject.GetInstanceID();

            try
            {
                fixture.Preview.SetParts(firstHead, body, legs);
                fixture.Preview.SetHead(replacementHead);

                Assert.That(fixture.Head.sprite, Is.SameAs(replacementHead));
                Assert.That(fixture.Body.sprite, Is.SameAs(body));
                Assert.That(fixture.Legs.sprite, Is.SameAs(legs));
                Assert.That(fixture.Head.gameObject.GetInstanceID(), Is.EqualTo(headObjectId));

                fixture.Preview.SetBody(null);

                Assert.That(fixture.Body.enabled, Is.False);
                Assert.That(fixture.Head.sprite, Is.SameAs(replacementHead));
                Assert.That(fixture.Legs.sprite, Is.SameAs(legs));
                Assert.That(fixture.Background.enabled, Is.True);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BackgroundCanBeReplacedWithoutChangingParts()
        {
            PreviewFixture fixture = new PreviewFixture();
            Sprite background = fixture.CreateSprite();
            Sprite head = fixture.CreateSprite();

            try
            {
                fixture.Preview.SetHead(head);
                fixture.Preview.SetCardBackground(background);

                Assert.That(fixture.Background.sprite, Is.SameAs(background));
                Assert.That(fixture.Background.enabled, Is.True);
                Assert.That(fixture.Head.sprite, Is.SameAs(head));

                fixture.Preview.SetCardBackground(null);

                Assert.That(fixture.Background.sprite, Is.Null);
                Assert.That(fixture.Background.enabled, Is.True);
                Assert.That(fixture.Head.sprite, Is.SameAs(head));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private sealed class PreviewFixture
        {
            private readonly GameObject root;
            private readonly System.Collections.Generic.List<Texture2D> textures = new();
            private readonly System.Collections.Generic.List<Sprite> sprites = new();

            public PreviewFixture()
            {
                root = new GameObject("CardPreview", typeof(RectTransform));
                Background = CreateImage("CardBackground");
                Legs = CreateImage("LegsSlot");
                Body = CreateImage("BodySlot");
                Head = CreateImage("HeadSlot");
                Preview = root.AddComponent<UICardPreview>();
                Preview.BindSlots(Background, Legs, Body, Head);
            }

            public UICardPreview Preview { get; }
            public Image Background { get; }
            public Image Legs { get; }
            public Image Body { get; }
            public Image Head { get; }

            public Sprite CreateSprite()
            {
                var texture = new Texture2D(2, 2);
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
                textures.Add(texture);
                sprites.Add(sprite);
                return sprite;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(root);
                for (int i = 0; i < sprites.Count; i++)
                {
                    Object.DestroyImmediate(sprites[i]);
                }

                for (int i = 0; i < textures.Count; i++)
                {
                    Object.DestroyImmediate(textures[i]);
                }
            }

            private Image CreateImage(string name)
            {
                var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                child.transform.SetParent(root.transform, false);
                return child.GetComponent<Image>();
            }
        }
    }
}
