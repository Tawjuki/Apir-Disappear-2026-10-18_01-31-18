const { DataApi } = require('@unity-services/cloud-save-1.4');

const CARD_ID = /^[0-9a-f]{32}$/;
const MAX_BYTES = 48 * 1024;
const RECORD_KEY = 'record';
const MAX_ID_LENGTH = 128;
const NORMAL_SKILLS = new Set([
    'normal_palo', 'normal_pahinga', 'normal_sunod_sunod', 'normal_panggulo', 'normal_suwerte'
]);
const PARTS = Object.freeze({
    part_common_magsasaka_head: part('Head', 'Common', 1, 15, 12, 0, 0, []),
    part_common_magsasaka_body: part('Body', 'Common', 2, 25, 13, 0, 0, []),
    part_common_magsasaka_legs: part('Legs', 'Common', 4, 12, 10, 5, 0, []),
    part_rare_abogado_head: part('Head', 'Rare', 1, 30, 25, 0, 15,
        ['passive_matibay_na_isip']),
    part_rare_abogado_body: part('Body', 'Rare', 2, 42, 23, 0, 0,
        ['passive_huling_depensa']),
    part_rare_abogado_legs: part('Legs', 'Rare', 4, 26, 19, 5, 0, []),
    part_epic_bakunawa_head: part('Head', 'Epic', 1, 70, 50, 0, 10,
        ['passive_ngipin_ng_dragon']),
    part_epic_bakunawa_body_legs: part('Body', 'Epic', 6, 115, 74, 15, 0,
        ['passive_balat_ng_dragon_sunog', 'passive_balat_ng_dragon_laban',
         'passive_kaliskis_ng_bakunawa'])
});

function part(slot, rarity, regions, hp, attack, speed, critical, passives) {
    return Object.freeze({ slot, rarity, regions, hp, attack, speed, critical, passives });
}

function isShortString(value, allowEmpty = false) {
    return typeof value === 'string' && value.length <= MAX_ID_LENGTH &&
        (allowEmpty || value.trim().length > 0);
}

function isFiniteNumber(value) {
    return typeof value === 'number' && Number.isFinite(value);
}

function isValidPart(part) {
    if (part === null || part === undefined) return true;
    const expected = PARTS[part.definitionId];
    return expected !== undefined && part.slot === expected.slot &&
        part.visualAssetId === part.definitionId && part.rarity === expected.rarity &&
        part.occupiedRegions === expected.regions && isFiniteNumber(part.offsetX) &&
        isFiniteNumber(part.offsetY) && isFiniteNumber(part.scale) && part.scale > 0 &&
        Number.isInteger(part.layerOrder);
}

function isValidStatistics(stats) {
    return stats && Number.isInteger(stats.maximumHitPoints) && stats.maximumHitPoints > 0 &&
        Number.isInteger(stats.effectiveAttack) && stats.effectiveAttack >= 0 &&
        isFiniteNumber(stats.attackBonusPercent) && isFiniteNumber(stats.speedBonusPercent) &&
        isFiniteNumber(stats.criticalChanceBonusPercent) && Array.isArray(stats.passiveIds) &&
        stats.passiveIds.every(id => isShortString(id)) && Array.isArray(stats.modifierIds) &&
        stats.modifierIds.every(id => isShortString(id)) && Array.isArray(stats.modifierValues) &&
        stats.modifierValues.every(isFiniteNumber) &&
        stats.modifierIds.length === stats.modifierValues.length;
}

function sameValues(actual, expected) {
    return Array.isArray(actual) && actual.length === expected.length &&
        actual.every((value, index) => value === expected[index]);
}

function hasAuthoritativeComposition(record) {
    const selected = [record.head, record.body, record.legs].filter(value => value != null);
    let occupied = 0;
    let hp = 0;
    let attack = 0;
    let speed = 0;
    let critical = 0;
    const passives = [];
    for (const selectedPart of selected) {
        const definition = PARTS[selectedPart.definitionId];
        if (!definition || (occupied & definition.regions) !== 0) return false;
        occupied |= definition.regions;
        hp += definition.hp;
        attack += definition.attack;
        speed += definition.speed;
        critical += definition.critical;
        passives.push(...definition.passives);
    }

    const stats = record.statistics;
    return stats.maximumHitPoints === 100 + Math.round(hp) &&
        stats.effectiveAttack === Math.round(10 * (1 + attack / 100)) &&
        stats.attackBonusPercent === attack && stats.speedBonusPercent === speed &&
        stats.criticalChanceBonusPercent === critical &&
        sameValues(stats.passiveIds, passives) && sameValues(stats.modifierIds, []) &&
        sameValues(stats.modifierValues, []);
}

module.exports = async ({ params, context, logger }) => {
    const cardId = String(params.cardId || '').trim().toLowerCase();
    const recordJson = String(params.recordJson || '');
    if (!CARD_ID.test(cardId) || Buffer.byteLength(recordJson, 'utf8') > MAX_BYTES) {
        throw new Error('Invalid AR card request.');
    }

    let record;
    try { record = JSON.parse(recordJson); }
    catch (_) { throw new Error('Card record is not valid JSON.'); }

    if (!isShortString(context.playerId) || record.schemaVersion !== 1 || record.cardId !== cardId ||
        record.ownerPlayerId !== context.playerId ||
        typeof record.sourceAssembledCardId !== 'string' ||
        !/^standalone-[0-9a-f]{32}$/.test(record.sourceAssembledCardId) ||
        !Number.isInteger(record.sourceRevision) || record.sourceRevision < 0 ||
        !Number.isInteger(record.createdUnixMilliseconds) || record.createdUnixMilliseconds <= 0 ||
        typeof record.cardName !== 'string' || record.cardName.trim().length < 1 ||
        record.cardName.trim().length > 32 ||
        !Array.isArray(record.selectedSkillIds) || record.selectedSkillIds.length > 4 ||
        new Set(record.selectedSkillIds).size !== record.selectedSkillIds.length ||
        !record.selectedSkillIds.every(id => NORMAL_SKILLS.has(id)) ||
        (record.head != null && record.head.slot !== 'Head') ||
        (record.body != null && record.body.slot !== 'Body') ||
        (record.legs != null && record.legs.slot !== 'Legs') ||
        !isValidPart(record.head) || !isValidPart(record.body) || !isValidPart(record.legs) ||
        !isValidStatistics(record.statistics) || !hasAuthoritativeComposition(record)) {
        throw new Error('Card record validation failed.');
    }

    const api = new DataApi(context);
    const existing = await api.getCustomItems(context.projectId, cardId, [RECORD_KEY]);
    const value = existing.data.results[0]?.value;
    if (value !== undefined) {
        if (value !== recordJson) return { saved: false, duplicate: false, conflict: true, cardId };
        return { saved: false, duplicate: true, cardId };
    }

    await api.setCustomItem(context.projectId, cardId, { key: RECORD_KEY, value: recordJson });
    logger.info('Saved shared APIR APIR card.', { cardId, ownerPlayerId: context.playerId });
    return { saved: true, duplicate: false, conflict: false, cardId };
};

module.exports.params = {
    cardId: { type: 'String', required: true },
    recordJson: { type: 'String', required: true }
};
