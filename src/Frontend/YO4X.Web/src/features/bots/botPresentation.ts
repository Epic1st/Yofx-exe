import type { BotView } from '../../api/contracts';

const mq5Suffix = /\.mq5$/iu;
const yo4xSuffix = /\.yo4x$/iu;

function botStem(name: string): string {
  return name.trim().replace(mq5Suffix, '').replace(yo4xSuffix, '').toLocaleLowerCase('en-US');
}

function duplicateKey(bot: BotView): string {
  const identityName = yo4xSuffix.test(bot.strategyName.trim()) ? bot.strategyName : bot.name;
  return [botStem(identityName), bot.brokerAccountId, bot.symbol.toLocaleLowerCase('en-US'), bot.host].join('::');
}

function isPackaged(bot: BotView): boolean {
  return yo4xSuffix.test(bot.strategyName.trim()) || yo4xSuffix.test(bot.name.trim());
}

/** Hide a stopped legacy/source twin once the packaged bot exists on the same account. */
export function visibleBots(bots: readonly BotView[]): readonly BotView[] {
  const packagedKeys = new Set(
    bots.filter(isPackaged).map(duplicateKey),
  );
  return bots.filter((bot) =>
    isPackaged(bot)
    || bot.status === 'RUNNING'
    || bot.status === 'STARTING'
    || !packagedKeys.has(duplicateKey(bot)));
}

/** Use the package strategy name even when the persisted bot name still ends in .mq5. */
export function displayBotName(bot: BotView): string {
  const name = bot.name.trim();
  if (isPackaged(bot)) return bot.strategyName.trim();
  return name;
}
