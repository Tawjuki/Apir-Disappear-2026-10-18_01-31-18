using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public sealed class SkillCatalog
    {
        private readonly Dictionary<string, SkillDefinition> definitions;
        private readonly SkillDefinition[] normalSkills;
        private readonly SkillDefinition[] soulSkills;

        public SkillCatalog(IEnumerable<SkillDefinition> definitions)
        {
            this.definitions = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
            var normal = new List<SkillDefinition>();
            var soul = new List<SkillDefinition>();
            foreach (SkillDefinition definition in definitions ??
                     throw new ArgumentNullException(nameof(definitions)))
            {
                if (definition == null || !this.definitions.TryAdd(definition.Id, definition))
                {
                    throw new ArgumentException("Skill definitions must be unique and non-null.");
                }

                if (definition.Category == SkillCategory.Normal) normal.Add(definition);
                if (definition.Category == SkillCategory.Soul) soul.Add(definition);
            }

            normalSkills = normal.ToArray();
            soulSkills = soul.ToArray();
        }

        public IReadOnlyDictionary<string, SkillDefinition> Definitions => definitions;
        public IReadOnlyList<SkillDefinition> NormalSkills => normalSkills;
        public IReadOnlyList<SkillDefinition> SoulSkills => soulSkills;

        public bool TryGet(string skillId, out SkillDefinition definition) =>
            definitions.TryGetValue(skillId ?? string.Empty, out definition);

        public IReadOnlyList<SkillDefinition> GetSoulSkills(string soulId)
        {
            var result = new List<SkillDefinition>(5);
            for (int i = 0; i < soulSkills.Length; i++)
            {
                if (string.Equals(soulSkills[i].SoulId, soulId, StringComparison.Ordinal))
                {
                    result.Add(soulSkills[i]);
                }
            }

            return result.AsReadOnly();
        }
    }

    public static class DefaultSkillCatalog
    {
        public const string BasicAttackId = "normal_palo";
        public const string PahingaId = "normal_pahinga";
        public const string SunodSunodId = "normal_sunod_sunod";
        public const string PangguloId = "normal_panggulo";
        public const string SuwerteId = "normal_suwerte";

        public static SkillCatalog Create()
        {
            var skills = new List<SkillDefinition>(30);
            AddNormal(skills, BasicAttackId, "Palo!", "Performs one basic damaging attack.",
                new SkillExecutionDefinition(10, 1));
            AddNormal(skills, PahingaId, "Pahinga", "Immediately restores 6 HP.",
                new SkillExecutionDefinition(healing: 6), SkillTargetType.Self);
            AddNormal(skills, SunodSunodId, "Sunod-Sunod!", "Performs two consecutive attacks.",
                new SkillExecutionDefinition(4, 2));
            AddNormal(skills, PangguloId, "Panggulo!", "May apply a random negative status.",
                new SkillExecutionDefinition(6, 1, specialRule: SkillSpecialRule.RandomNegativeStatus,
                    specialProbability: 0.25f));
            AddNormal(skills, SuwerteId, "Suwerte!", "May grant a random positive status.",
                new SkillExecutionDefinition(specialRule: SkillSpecialRule.RandomPositiveStatus,
                    specialProbability: 0.25f), SkillTargetType.Self);

            AddSoul(skills, SoulIds.Kanayunan, 1, "sibat_ng_kawayan", "Sibat ng Kawayan", 1,
                new SkillExecutionDefinition(16, 1));
            AddSoul(skills, SoulIds.Kanayunan, 2, "gumagapang_na_ugat", "Gumagapang na Ugat", 2,
                new SkillExecutionDefinition(7, 2));
            AddSoul(skills, SoulIds.Kanayunan, 3, "ani_ng_lakas", "Ani ng Lakas", 3,
                new SkillExecutionDefinition(12, 1, statusApplications: Self(StatusEffectIds.Lakas, 0.8f)));
            AddSoul(skills, SoulIds.Kanayunan, 4, "pahinga_sa_lilim", "Pahinga sa Lilim", 3,
                new SkillExecutionDefinition(statusApplications: Self(StatusEffectIds.Pagsigla, 1f)),
                SkillTargetType.Self);
            AddSoul(skills, SoulIds.Kanayunan, 5, "lason_ng_dahon", "Lason ng Dahon", 3,
                new SkillExecutionDefinition(10, 1, statusApplications: Enemy(StatusEffectIds.Lason, 0.4f)));

            AddSoul(skills, SoulIds.Dambana, 1, "ilaw_ng_dambana", "Ilaw ng Dambana", 2,
                new SkillExecutionDefinition(19, 1));
            AddSoul(skills, SoulIds.Dambana, 2, "panata", "Panata", 3,
                new SkillExecutionDefinition(healing: 8, specialRule: SkillSpecialRule.CleanseOneNegative),
                SkillTargetType.Self);
            AddSoul(skills, SoulIds.Dambana, 3, "basbas", "Basbas", 3,
                new SkillExecutionDefinition(statusApplications: Self(StatusEffectIds.Laban, 1f)),
                SkillTargetType.Self);
            AddSoul(skills, SoulIds.Dambana, 4, "sindak_ng_kandila", "Sindak ng Kandila", 3,
                new SkillExecutionDefinition(10, 1, statusApplications: Enemy(StatusEffectIds.Sunog, 0.4f)));
            AddSoul(skills, SoulIds.Dambana, 5, "alay", "Alay", 3,
                new SkillExecutionDefinition(28, 1, specialRule: SkillSpecialRule.SacrificeHitPoints,
                    specialValue: 10));

            AddSoul(skills, SoulIds.Katubigan, 1, "daloy_ng_tubig", "Daloy ng Tubig", 2,
                new SkillExecutionDefinition(statusApplications: Self(StatusEffectIds.Bilis, 1f)),
                SkillTargetType.Self);
            AddSoul(skills, SoulIds.Katubigan, 2, "tilamsik", "Tilamsik", 2,
                new SkillExecutionDefinition(5, 3));
            AddSoul(skills, SoulIds.Katubigan, 3, "yelong_ambon", "Yelong Ambon", 3,
                new SkillExecutionDefinition(10, 1, statusApplications: Enemy(StatusEffectIds.Lamig, 0.45f)));
            AddSoul(skills, SoulIds.Katubigan, 4, "basang_sipa", "Basang Sipa", 3,
                new SkillExecutionDefinition(14, 1, statusApplications: Enemy(StatusEffectIds.Basa, 0.4f)));
            AddSoul(skills, SoulIds.Katubigan, 5, "ulan_ulan", "Ulan-Ulan", 3,
                new SkillExecutionDefinition(3, 4));

            AddSoul(skills, SoulIds.Pamayanan, 1, "sabay_sabay", "Sabay-Sabay!", 2,
                new SkillExecutionDefinition(8, 2));
            AddSoul(skills, SoulIds.Pamayanan, 2, "bayanihan", "Bayanihan", 2,
                new SkillExecutionDefinition(statusApplications: Self(StatusEffectIds.Lakas, 1f)),
                SkillTargetType.Self);
            AddSoul(skills, SoulIds.Pamayanan, 3, "ganti_ng_kapitbahay", "Ganti ng Kapitbahay", 2,
                new SkillExecutionDefinition(14, 1,
                    specialRule: SkillSpecialRule.PreviousExchangeRetaliation, alternateDamage: 23));
            AddSoul(skills, SoulIds.Pamayanan, 4, "barikada_ng_barangay", "Barikada ng Barangay", 3,
                new SkillExecutionDefinition(statusApplications: Self(StatusEffectIds.Laban, 1f)),
                SkillTargetType.Self);
            AddSoul(skills, SoulIds.Pamayanan, 5, "batong_kalye", "Batong Kalye", 3,
                new SkillExecutionDefinition(12, 1, statusApplications: Enemy(StatusEffectIds.Sugat, 0.55f)));

            AddSoul(skills, SoulIds.Aliwanan, 1, "pitik_sa_ilong", "Pitik sa Ilong", 1,
                new SkillExecutionDefinition(13, 1));
            AddSoul(skills, SoulIds.Aliwanan, 2, "alikabok", "Alikabok!", 2,
                new SkillExecutionDefinition(9, 1, statusApplications: Enemy(StatusEffectIds.Madumi, 0.5f)));
            AddSoul(skills, SoulIds.Aliwanan, 3, "tamang_tira", "Tamang Tira!", 3,
                new SkillExecutionDefinition(statusApplications: Self(StatusEffectIds.Talas, 1f)),
                SkillTargetType.Self);
            AddSoul(skills, SoulIds.Aliwanan, 4, "laruang_kuryente", "Laruang Kuryente", 4,
                new SkillExecutionDefinition(7, 1, statusApplications: Enemy(StatusEffectIds.Kuryente, 0.35f)));
            AddSoul(skills, SoulIds.Aliwanan, 5, "paikot_ulo", "Paikot-Ulo", 3,
                new SkillExecutionDefinition(8, 1, statusApplications: Enemy(StatusEffectIds.Paghihilo, 0.4f)));
            return new SkillCatalog(skills);
        }

        private static void AddNormal(
            ICollection<SkillDefinition> target,
            string id,
            string name,
            string description,
            SkillExecutionDefinition execution,
            SkillTargetType targetType = SkillTargetType.Enemy)
        {
            target.Add(new SkillDefinition(
                id, SkillCategory.Normal, 0, Array.Empty<ISkillEffect>(), name, description,
                targetType: targetType, execution: execution));
        }

        private static void AddSoul(
            ICollection<SkillDefinition> target,
            string soulId,
            int rank,
            string suffix,
            string name,
            int cooldown,
            SkillExecutionDefinition execution,
            SkillTargetType targetType = SkillTargetType.Enemy)
        {
            target.Add(new SkillDefinition(
                "soul_" + soulId + "_" + suffix,
                SkillCategory.Soul,
                cooldown,
                Array.Empty<ISkillEffect>(),
                name,
                name + ".",
                soulId,
                rank,
                rank * 5,
                targetType,
                execution));
        }

        private static SkillStatusApplication[] Enemy(string id, float chance) =>
            new[] { new SkillStatusApplication(id, chance, SkillTargetType.Enemy) };

        private static SkillStatusApplication[] Self(string id, float chance) =>
            new[] { new SkillStatusApplication(id, chance, SkillTargetType.Self) };
    }
}
