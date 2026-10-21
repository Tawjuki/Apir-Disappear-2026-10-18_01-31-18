const { DataApi } = require('@unity-services/cloud-save-1.4');

const CARD_ID = /^[0-9a-f]{32}$/;
const MAX_BYTES = 48 * 1024;
const RECORD_KEY = 'record';
const MAX_ID_LENGTH = 128;

function isShortString(value, allowEmpty = false) {
    return typeof value === 'string' && value.length <= MAX_ID_LENGTH &&
        (allowEmpty || value.trim().length > 0);
}

function isFiniteNumber(value) {
    return typeof value === 'number' && Number.isFinite(value);
}

function isValidPart(part) {
    return part === null || part === undefined ||
        (isShortString(part.slot) && isShortString(part.definitionId) &&
         isShortString(part.visualAssetId) && isShortString(part.rarity, true) &&
         Number.isInteger(part.occupiedRegions) && part.occupiedRegions >= 0 &&
         isFiniteNumber(part.offsetX) && isFiniteNumber(part.offsetY) &&
         isFiniteNumber(part.scale) && part.scale > 0 && Number.isInteger(part.layerOrder));
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

module.exports = async ({ params, context, logger }) => {
    const cardId = String(params.cardId || '').trim().toLowerCase();
    const recordJson = String(params.recordJson || '');
    if (!CARD_ID.test(cardId) || Buffer.byteLength(recordJson, 'utf8') > MAX_BYTES) {
        throw new Error('Invalid AR card request.');
    }

    let record;
    try { record = JSON.parse(recordJson); }
    catch (_) { throw new Error('Card record is not valid JSON.'); }

    if (record.schemaVersion !== 1 || record.cardId !== cardId ||
        record.ownerPlayerId !== context.playerId ||
        !isShortString(record.sourceAssembledCardId) || !Number.isInteger(record.sourceRevision) ||
        record.sourceRevision < 0 || typeof record.cardName !== 'string' || record.cardName.trim().length < 1 ||
        record.cardName.trim().length > 32 ||
        !Array.isArray(record.selectedSkillIds) || record.selectedSkillIds.length > 4 ||
        new Set(record.selectedSkillIds).size !== record.selectedSkillIds.length ||
        !record.selectedSkillIds.every(id => isShortString(id)) ||
        !isValidPart(record.head) || !isValidPart(record.body) || !isValidPart(record.legs) ||
        !isValidStatistics(record.statistics)) {
        throw new Error('Card record validation failed.');
    }

    const api = new DataApi(context);
    const existing = await api.getCustomItems(context.projectId, cardId, [RECORD_KEY]);
    const value = existing.data.results[0]?.value;
    if (value !== undefined) {
        if (value !== recordJson) throw new Error('Card ID collision.');
        return { saved: false, duplicate: true, cardId };
    }

    await api.setCustomItem(context.projectId, cardId, { key: RECORD_KEY, value: recordJson });
    logger.info('Saved shared APIR APIR card.', { cardId, ownerPlayerId: context.playerId });
    return { saved: true, duplicate: false, cardId };
};
