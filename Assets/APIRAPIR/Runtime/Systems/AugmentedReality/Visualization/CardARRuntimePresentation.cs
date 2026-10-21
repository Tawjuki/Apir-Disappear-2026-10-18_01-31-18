using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.AugmentedReality
{
    [Serializable]
    public sealed class ARCardSpriteBinding
    {
        public string visualAssetId;
        public Sprite sprite;
    }

    [DisallowMultipleComponent]
    public sealed class ARCardSpriteResolver : MonoBehaviour
    {
        [SerializeField] private ARCardSpriteBinding[] bindings = Array.Empty<ARCardSpriteBinding>();
        private Dictionary<string, Sprite> lookup;

        public Sprite Resolve(string visualAssetId)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                for (int i = 0; i < bindings.Length; i++)
                {
                    ARCardSpriteBinding value = bindings[i];
                    if (value != null && value.sprite != null && !string.IsNullOrWhiteSpace(value.visualAssetId))
                        lookup[value.visualAssetId.Trim()] = value.sprite;
                }
            }
            return visualAssetId != null && lookup.TryGetValue(visualAssetId, out Sprite sprite) ? sprite : null;
        }
    }

    [DisallowMultipleComponent]
    public sealed class ARCardDisplayView : MonoBehaviour
    {
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private UICardPreview preview;
        [SerializeField] private TMP_Text cardNameText;
        [SerializeField] private TMP_Text cardIdText;
        [SerializeField] private TMP_Text skillsText;
        [SerializeField] private TMP_Text statisticsText;
        [SerializeField] private TMP_Text classificationText;
        [SerializeField] private Image qrImage;

        private Texture2D qrTexture;
        private Sprite qrSprite;

        public void Configure(
            GameObject root,
            UICardPreview cardPreview,
            TMP_Text cardName,
            TMP_Text cardId,
            TMP_Text skills,
            TMP_Text statistics,
            TMP_Text classification,
            Image qr)
        {
            contentRoot = root;
            preview = cardPreview;
            cardNameText = cardName;
            cardIdText = cardId;
            skillsText = skills;
            statisticsText = statistics;
            classificationText = classification;
            qrImage = qr;
        }

        public void Display(ARCardRecord record, ARCardSpriteResolver sprites)
        {
            if (record == null || !record.IsValidBasicShape())
            {
                Hide();
                return;
            }

            if (preview != null)
            {
                preview.SetParts(
                    ResolvePart(record.head, sprites),
                    ResolvePart(record.body, sprites),
                    ResolvePart(record.legs, sprites));
                preview.ApplyLayout();
            }
            if (cardNameText != null) cardNameText.text = record.cardName;
            if (cardIdText != null) cardIdText.text = record.cardId;
            if (statisticsText != null)
            {
                ARCardStatisticsRecord stats = record.statistics;
                statisticsText.text = stats == null ? string.Empty :
                    $"HP {stats.maximumHitPoints}  ATK {stats.effectiveAttack}  " +
                    $"ATK +{stats.attackBonusPercent:0.#}%  SPD +{stats.speedBonusPercent:0.#}%  " +
                    $"CRIT +{stats.criticalChanceBonusPercent:0.#}%";
            }
            if (skillsText != null) skillsText.text = BuildSkills(record.selectedSkillIds);
            if (classificationText != null) classificationText.text = BuildClassification(record);
            DisplayQr(record.cardId);
            if (contentRoot != null) contentRoot.SetActive(true);
        }

        public void Hide()
        {
            if (contentRoot != null) contentRoot.SetActive(false);
        }

        private void DisplayQr(string cardId)
        {
            ReleaseQr();
            if (qrImage == null) return;
            qrTexture = new ZXingQRCodeCodec().CreateCardIdTexture(cardId, 256);
            qrSprite = Sprite.Create(qrTexture, new Rect(0f, 0f, qrTexture.width, qrTexture.height),
                new Vector2(0.5f, 0.5f), 100f);
            qrImage.sprite = qrSprite;
            qrImage.preserveAspect = true;
            qrImage.enabled = true;
        }

        private void ReleaseQr()
        {
            if (qrSprite != null) Destroy(qrSprite);
            if (qrTexture != null) Destroy(qrTexture);
            qrSprite = null;
            qrTexture = null;
            if (qrImage != null) qrImage.sprite = null;
        }

        private void OnDestroy()
        {
            ReleaseQr();
        }

        private static Sprite ResolvePart(ARCardPartRecord part, ARCardSpriteResolver resolver) =>
            part == null || resolver == null ? null : resolver.Resolve(part.visualAssetId);

        private static string BuildSkills(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0) return "Skills: None";
            SkillCatalog catalog = DefaultSkillCatalog.Create();
            var names = new string[ids.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = catalog.TryGet(ids[i], out SkillDefinition skill) ? skill.DisplayName : ids[i];
            return "Skills: " + string.Join(", ", names);
        }

        private static string BuildClassification(ARCardRecord record)
        {
            var values = new List<string>(3);
            AddClassification(record.head, values);
            AddClassification(record.body, values);
            AddClassification(record.legs, values);
            return values.Count == 0 ? "Blank Card" : string.Join(" • ", values);
        }

        private static void AddClassification(ARCardPartRecord part, ICollection<string> target)
        {
            if (part == null) return;
            string hybrid = (part.occupiedRegions & 6) == 6 ? " Hybrid Body & Legs" : " " + part.slot;
            target.Add((string.IsNullOrWhiteSpace(part.rarity) ? "Part" : part.rarity) + hybrid);
        }
    }
}
