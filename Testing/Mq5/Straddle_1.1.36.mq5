//+------------------------------------------------------------------+
//|                                               Straddle_1.1.36.mq5|
//|        Broker-agnostic XAUUSD pending-order grid ("straddle") EA |
//|                                                  Version: 1.1.36|
//+------------------------------------------------------------------+
//| v1.1.36 DAILY PROFIT/LOSS PARITY + EQUITY-ONLY MULTI-DAY PAUSE     |
//| - Daily equity P/L formula unchanged (equity - day-start equity).  |
//| - Daily PROFIT stop: rest-of-day only (same as 1.1.34).            |
//| - Multi-day no-trade arms ONLY on daily-EQUITY loss limit hit.     |
//| - Open-book hard stop still force-closes for the rest of that day  |
//|   but does NOT start a multi-day cooldown when day equity is green.|
//| - Tester: clear leftover multi-day markers at OnInit (no bleed).   |
//| - FIX: InpDailyLimitsUsePercent lets AutoDaily*Percent apply even  |
//|   with fixed lots (was tied only to auto lot mode → $150 early stop).|
//| - OnInit WARNs when 15% is set but fixed-USD caps are the ones used.|
//| v1.1.35 DAILY LOSS MULTI-DAY NO-TRADE COOLDOWN                     |
//| - After HARD daily LOSS limit: force-close as before, then pause.  |
//| - Input: do not trade for 1 / 2 / 3 / 4 server days (user select). |
//| - Resume automatically when the chosen number of days has passed.  |
//| - Restart-safe via terminal global variable (login+magic+symbol).  |
//| v1.1.34 MARKET-SAFE ASCII INPUT GROUP LABELS                       |
//| - Keeps the 1.1.33 freeze-safe close behavior unchanged.            |
//| - Uses ASCII-only group labels for the Market input parser.          |
//| v1.1.33 FREEZE-SAFE POSITION REDUCTION FOR MARKET VALIDATION       |
//| - Defers market closes when the position/SL/TP is inside freeze.    |
//| - Applies to daily hard-close, teardown, and cleanup reductions.   |
//| v1.1.32 METAQUOTES MARKET VALIDATION FOR EUR/GBP/JPY FX           |
//| - Uses currency metadata and SymbolInfoTick for low-priced FX.     |
//| - Never rejects a symbol because a normalized price step rounds.    |
//| - Chart UI is enabled by default and remains user-switchable.      |
//| v1.1.30 SYMMETRIC MARGIN-LIMITED GRID BUILD                      |
//| - Preflights both sides of each empty level before placement.     |
//| - A margin cap stops before a pair, never after one side only.     |
//| - Logs exact capped level and omitted BUY/SELL pair range.        |
//| - Cycle summary distinguishes margin cap from broker order cap.   |
//| v1.1.29 BALANCE-AUTO LOTS AND MONEY LIMITS                       |
//| - One lot-mode selector: fixed tier lots or auto balance lots.   |
//| - Auto reference: $2,000 => 0.01 / 0.05 / 0.10 lots.             |
//| - Auto cycle target and daily limits use percentage inputs.       |
//| - Fixed mode keeps dollar target and daily dollar limits.         |
//| v1.1.28 PRICE-PROPORTIONAL GRID STEP                             |
//| - At reference price 5000, InpGridStepUSD=2 means a $2 step.     |
//| - Live step scales linearly with price for new cycles.            |
//| v1.1.27 DAILY DD TRUE/FALSE HARD-STOP CONTROL                     |
//| - InpUseDailyLimits=true: 1.1.22-style hard close and day stop.   |
//| - InpUseDailyLimits=false: disable the daily account DD limits.  |
//| v1.1.25 EVEN TIER DISTRIBUTION                                    |
//| - Near/mid/far tiers split active levels as evenly as possible.   |
//| - 100 levels now map to 33/33/34, not 4/4/92.                     |
//| v1.1.24 STRICT DAILY EQUITY + OPEN-BOOK HARD STOP                 |
//| - Either daily account-equity P/L OR EA floating P/L can trigger. |
//| - Input +profit / -loss thresholds force-close and stop the day.  |
//| - Fresh book snapshot is taken before evaluating the limits.       |
//| v1.1.23 DAILY EQUITY STOP HARDENING                              |
//| - Recovery-day lots and cycle target remain x2 after a -$700 day.|
//| - Daily profit stop stays fixed at +$300, including recovery day.|
//| - Daily force-close state survives restart/rollover until flat.   |
//| v1.1.22 HARD DAILY LOSS CAP (fix recovery-day 2x loss limit)     |
//| - LOG BUG: recovery day after loss doubled day loss lim 700->1400|
//|   so equity could drop -$1400 on a "2x day".                      |
//| - FIX: recovery x2 applies to LOTS + cycle TARGET only.          |
//|   InpDailyLossLimitUSD stays HARD 1x always (-700).              |
//| - Daily force-close also closes Float tickets (no leftover float).|
//| v1.1.21 DAILY EQUITY +$300 / -$700 STOP AND CLOSE (release)      |
//| - Day P/L = equity - day-start equity (floating included).       |
//| - +300 day equity: CLOSE ALL + stop new trades for the day.      |
//| - -700 day equity: CLOSE ALL immediately + stop for the day.     |
//| - After loss stop: next day lots + cycle target x2; daily stops fixed.|
//| v1.1.20 DAILY LIMITS USE EQUITY + STOP AND CLOSE                  |
//| v1.1.19 NEXT-DAY 2x LOTS AFTER DAILY LOSS LIMIT                   |
//| - InpDailyLossNextDayLot2x: next day base lots x2 after loss day.|
//| v1.1.17 RECOMMENDED DEFAULTS (based on 1.1.16 + safe set)         |
//| - Same engine as 1.1.16 (margin-abort, price trail, 2x upgrade). |
//| - Defaults: grid 30 / step $2 / target $60 / 2x thr $800.        |
//| - Avoids thr=200 + grid 50 that drove float to -$14k in logs.    |
//| - No RAPID-EQ / micro-bank (those were removed post-1.1.16).     |
//| v1.1.16 TESTER SPEED: margin-abort (fix multi-GB stuck logs)     |
//| - On REJECT_MARGIN_PROJECTION: abort rest of PopulateGrid this   |
//|   tick, set g_nextBuildTry backoff, ONE throttled log (not per   |
//|   level). Stops 60-level * every-tick reject storms.             |
//| v1.1.15 PRICE-TRAIL + NO BALANCE-DIP RECOVERY (minimal)          |
//| - TrailArmSteps = PRICE grids of profit to ARM trail (not count  |
//|   of open positions). Example: Arm=5, GridStep=$1 => arm after  |
//|   +$5 favorable move; first SL uses 5-grid distance.             |
//| - TrailStepN = trail distance after arm (2 or 1 preferred).      |
//| - TREND-2X profit-cleanup + $5 fast-bank OFF: realizing losers   |
//|   was dropping balance when recovery fired (log-confirmed).      |
//| - Keep 1x->2x pending upgrade only (no forced red closes).       |
//| v1.1.14 TREND-2X UPGRADE (pendings 1x->2x when armed)             |
//| v1.1.13 TRAIL ALWAYS (fix "trail not working")                   |
//| - InpTrailOnlyIfEquityGreen default OFF. That gate required      |
//|   equity >= balance+$2 before ANY trail; grid float DD blocked   |
//|   winners forever. Now in-profit legs trail even if equity red.  |
//| - InpTrailArmSteps default 1 (was 2) so first winner can trail.  |
//| v1.1.12 TREND DOUBLE-LOT (no extra recovery orders)              |
//| - NO recovery orders (basket RCV stays off).                     |
//| - If floating loss > InpTrendDoubleLotLossUSD (default 500) AND  |
//|   trend is confirmed (same lookback/move as trend detector):     |
//|   new/refill grid stops at FIXED level prices use 2x TierLot.    |
//| - With-trend side only is doubled (up => buy slots 2x, down =>   |
//|   sell slots 2x) so opposite side is not doubled into the trend. |
//| - Main grid stays; keep-pendings / pure trail rules from 1.1.11. |
//| v1.1.11 PURE ORIGINAL STRADDLE (user Hindi spec restored)        |
//| HARD RULES:                                                      |
//| 1) Fixed grid: BUY stops ABOVE price, SELL stops BELOW.          |
//| 2) Lots near/mid/far by level (0.01 / 0.02 / 0.03 typical).      |
//| 3) Price runs up/down days: trail ONLY in-profit legs; book when |
//|    trailing SL hits. Refill that slot with the same fixed stop.  |
//| 4) If ONE leg trails out but other legs still open: do NOT close |
//|    the whole basket until TOTAL net is in profit (Target).      |
//| 5) Optional: trail only when equity is slightly above balance.   |
//| 6) Keep grid pendings on chart; extra engines OFF by default.    |
//| v1.1.10 PRODUCTION CHANGELOG (BASKET BREAK-EVEN RECOVERY)        |
//| User model: sell trail/TP banking on a drop is fine. When price   |
//| rises and BUY stops fill, RECOVER those buys by placing SELL      |
//| stops in the buy zone as price climbs, adding layers until the    |
//| open-book floating P/L is back near 0 (break-even). Symmetric for |
//| sell-side when price falls. Does NOT wipe the main grid.          |
//| - InpUseBasketRecovery + target 0 + max recovery stops + lot.     |
//| - Comments STR RCV B / STR RCV S (separate from STR B#/S# grid).  |
//| v1.1.9 PRODUCTION CHANGELOG (KEEP GRID PENDINGS ON CHART)        |
//| - InpKeepGridPendings (default true): buy/sell stops stay on the |
//|   fixed price grid; recovery/float/EQ paths must NOT DeleteAll.  |
//| - Float re-anchor keeps anchor+step+pendings (no grid wipe).     |
//| - Always refill empty slots (populate not blocked by EQ/float).  |
//| - No counter-trend cancel; off-grid cleanup only kills garbage.  |
//| - Full target teardown still clears pendings (cycle bank only).  |
//| v1.1.8 PRODUCTION CHANGELOG (FAST EQUITY RECOVERY)               |
//| HONEST: equity = balance + floating. Equity only "recovers fast" |
//| if (a) price reverses, or (b) we CLOSE reds (balance may dip) or  |
//| (c) we STOP adding risk while harvesting winners. NeverRealize   |
//| alone keeps reds open for days => equity stays crushed.          |
//| - InpFastEquityRecovery: when floating DD >= trigger, STOP new   |
//|   grid, DELETE pendings, ALLOW funded loser cleanup (overrides   |
//|   NeverRealize for cleanup only), FLOAT remaining stuck reds.    |
//| - Earlier float + tighter TR so DD cannot snowball to -3k.       |
//| v1.1.7 PRODUCTION CHANGELOG (Jan19 equity/stuck fixes)           |
//| - Cap trend-rescue max entry lot at 0.03 (was 0.08 stacking DD). |
//| - retcode 0 classified as BACKOFF + global trade-jam gate so we  |
//|   stop 16k OrderSend_failed spam and give the tester a breather. |
//| - FLOAT SLEEPER rescue fires when ANY red float exists (not only |
//|   float-only books) and deletes pendings to stop restack.        |
//| - Green float close: multi-retry + jam backoff; hard-log fails.  |
//| v1.1.6 PRODUCTION CHANGELOG (float sleeper escape)               |
//| - Root fix: 1.1.5 blocked StartCycle forever while float bag red |
//|   with NO recovery path => multi-day stuck trades, equity sink,  |
//|   tester "skipping" under StartCycle BLOCKED spam.               |
//| - FLOAT SLEEPER: float-only red book enters Trend Rescue toward  |
//|   covering the bag (not a full 60-level restack).                |
//| - After InpFloatSleeperMaxHours (default 24h), allow new cycles  |
//|   again so backtest cannot freeze for weeks on 3 sleeper tickets.|
//| - LogState block message STABLE (no floatPL in key text) => no   |
//|   per-tick spam that stalls the Strategy Tester.                 |
//| v1.1.5 PRODUCTION CHANGELOG (balance never-down mode)            |
//| - InpNeverRealizeLoss: blocks voluntary red closes.              |
//| - While floated bag red: block full-grid restack (refined 1.1.6).|
//| v1.1.4 PRODUCTION CHANGELOG (dense 60-level grid recovery)       |
//| - Early uncapped float (MaxFloatLots=0) so re-anchor never stuck |
//|   refusing 0.47>0.15 style caps on 60-level books.               |
//| - Partial float: if a positive cap is set, float worst losers    |
//|   first up to cap then re-anchor (never all-or-nothing refuse).  |
//| - Faster float dwell/DD, lower profit reserve, snappier trend    |
//|   rescue for equity lift while keeping frequent cycle banks.     |
//| v1.1.3 PRODUCTION CHANGELOG                                      |
//| - Clean efficient logging: state-change events only at level 1;  |
//|   per-tick spam throttled via LogState (key+msg, 60s default).   |
//| - Float re-anchor REFUSED throttled; cleanup emits one summary.  |
//| - Init dumps demoted to level 2; compact STATUS heartbeat 120s.  |
//| - InpMaxFloatLots default raised 0.15 -> 0.50 (fewer refuse).    |
//| v1.1.2 PRODUCTION CHANGELOG                                      |
//| - Simplified Inputs tab: ~20 public controls for account, grid,  |
//|   targets, trend bias, recovery on/off, and chart UI.            |
//| - All advanced Inp* knobs demoted to source-only const defaults  |
//|   (same names) so logic/ValidateProductionInputs need no renames.|
//| - User-safe defaults: flat lots, higher targets, trend pause ON, |
//|   Float re-anchor ON, RHG hedge OFF, equity hard-flatten OFF.    |
//| v1.1.1 PRODUCTION CHANGELOG                                      |
//| - Trend one-side grid: during a strong directional move, place/  |
//|   refill ONLY with-trend grid stop pendings (BUY-only in uptrend,|
//|   SELL-only in downtrend); cancel opposite-side grid pendings.   |
//| - Optional block of STR RHG hedges that would open AGAINST the   |
//|   trend (covers rescue hedge and equity-backstop via OpenNetHedge).|
//| - Reuses InpTrendPauseLookbackBars / InpTrendPauseMoveUSD for    |
//|   detection; one-side can run even when InpUseTrendPause=false.  |
//| - Existing filled counter-trend positions are left open; no      |
//|   force-close. Recovery remains via existing engines only.       |
//| v1.1.0 PRODUCTION CHANGELOG                                      |
//| - Orphan close/reopen, tags, marker and public inputs removed.   |
//| - Float Re-anchor is the only set-aside path; old Orphan setfile |
//|   fields are ignored and never migrate into Float implicitly.    |
//| - Five explicit Float fields added; InpMaxFloatLots is double.   |
//| - Public inputs reorganized and validated before mutable state.  |
//| - Review all eight Float settings and resave v1.1.0 setfiles.    |
//+------------------------------------------------------------------+
//| STRATEGY (0.0.31 - adaptive recovery architecture)                |
//|   A CYCLE has a FIXED anchor: the snapped mid price when the      |
//|   cycle starts (flat -> new cycle). Fixed level prices:           |
//|     BUY  level k = SnapPrice(anchor + k*GridStepUSD)  (k=1..N)    |
//|     SELL level k = SnapPrice(anchor - k*GridStepUSD)              |
//|   Side+level are encoded in the order/position COMMENT            |
//|   ("STR B5", "STR S5") so every leg is identifiable and           |
//|   recoverable after a restart.                                    |
//|   POPULATION (ONE rule = initial build AND refill): every tick    |
//|   of an active, tradeable cycle (not paused, not tearing down,    |
//|   net < target), each (side, level) slot that has NEITHER an      |
//|   open position NOR a pending order gets a stop order at its      |
//|   FIXED price. A slot whose fixed price is not currently a valid  |
//|   stop (wrong side of market / inside stops-freeze) is SKIPPED    |
//|   this tick and retried on later ticks - never re-centered.       |
//|   PER-LEG TRAILING (the profit-recycle engine): once a side has   |
//|   >= TrailArmSteps open positions it is trail-active; every OPEN  |
//|   IN-PROFIT position on a trail-active side trails its own peak   |
//|   (best favorable CLOSE price seen) by TrailStepN grid-steps via  |
//|   a real broker-side SL on that exact position ticket. When the   |
//|   server fills the SL, the slot is refilled at the same fixed     |
//|   level by the population rule.                                  |
//|   Losing legs and legs on a side below TrailArmSteps fills are    |
//|   HELD untouched - there is NO loss cap by design.                |
//|   PROFIT-FUNDED CLEANUP (0.0.11): there is still NO hard loss cap.|
//|   When the cycle has surplus profit above a reserve/cost buffer,  |
//|   the EA can spend that surplus on ticket-based partial closes of |
//|   the largest losing positions first. If the whole basket is at   |
//|   target while losers remain, cleanup gets first use of surplus;  |
//|   full teardown resumes only after losers are gone, cleanup is    |
//|   not possible, or the cleanup spend drops cycleNet below target. |
//|   If the cycle is net-negative, only realized closed-leg profit   |
//|   from this cycle can fund cleanup.                               |
//|   FULL CLOSE: when                                                |
//|     cycleNet = realized this cycle (deal history)                 |
//|              + floating (open positions)                          |
//|   reaches TargetUSD, the whole cycle is torn down (all positions  |
//|   closed, all pendings deleted), the EA pauses a few seconds,     |
//|   then a fresh cycle starts at a NEW anchor.                      |
//|   GRID-BOUNDARY RESCUE (0.0.14): if fresh mid exits the fixed     |
//|   cycle envelope [anchor - N*step, anchor + N*step] while         |
//|   cycleNet is below the close buffer, the EA deletes pendings     |
//|   only and holds positions in persistent rescue mode. Balance is  |
//|   protected from voluntary negative basket closes, while floating |
//|   equity risk remains open by design. Safely positive baskets     |
//|   still use persistent teardown. Losing cleanup during rescue is  |
//|   funded only from realized account balance gains above the       |
//|   persisted rescue anchor balance, reserve, and cost buffer.      |
//|   TREND RESCUE MODE (0.0.20): an unsafe break beyond the fixed    |
//|   grid can switch into a separate Trend Rescue Mode. The EA       |
//|   deletes stale pendings, holds the old basket, blocks the        |
//|   normal two-sided grid, and opens only controlled trend-side      |
//|   recovery entries until booked profit fully covers the floating  |
//|   loss plus costs/buffer. Only then can it close all and rebuild. |
//|   Before adding more recovery entries, booked profit harvested    |
//|   above the rescue anchor can incrementally close old losers;     |
//|   floating equity is never treated as cleanup funding.            |
//|   Pending orders are sent with SL=0 and TP=0; filled positions    |
//|   receive/ratchet a broker-side trailing SL only after arming.    |
//|                                                                  |
//| SAFETY GUARDS (per research spec)                                |
//|   - Refuses netting/exchange accounts (RETAIL_HEDGING only).     |
//|   - All load-bearing symbol/account properties read at runtime.  |
//|   - Every price snapped to SYMBOL_TRADE_TICK_SIZE (not point).   |
//|   - Filling mode detected at runtime (avoids retcode 10030).     |
//|   - Volumes normalized to SYMBOL_VOLUME_MIN/MAX/STEP.            |
//|   - Stops/freeze level respected when placing pendings.          |
//|   - Bounded retry wrapper branching on TRADE_RETCODE_* codes.    |
//|   - Commission NOT read from POSITION_COMMISSION (reads 0);      |
//|     realized commission comes from deal history (DEAL_COMMISSION |
//|     + DEAL_FEE on the close deals); the open-side estimate uses  |
//|     the per-lot fallback input.                                  |
//|   - Cycle state recovered purely from existing positions and     |
//|     pendings (magic+symbol) after a terminal/VPS restart.        |
//|   - OnTimer(1s) safety net so cycle management still runs        |
//|     during low-tick periods.                                     |
//|                                                                  |
//| 0.0.2 CHANGES (market-closed / log-flood fix)                    |
//|   - Tradeability gate (CanTrade) before any grid build: trade    |
//|     mode FULL, terminal/MQL/account trade allowed, and current   |
//|     time inside a quote/trade session.                           |
//|   - Back-off (InpRetrySeconds) after a 0-order or market-closed  |
//|     build attempt; a 0-order build is NOT a successful cycle.    |
//|   - "grid built" summary only when placed > 0; never per-level   |
//|     logging inside the build loop.                               |
//|   - CTrade journal spam silenced (LogLevel(LOG_LEVEL_NO)).       |
//|   - InpLogLevel (0=errors,1=normal,2=debug) routed through Log() |
//|     with the untradeable notice throttled to state changes.      |
//|                                                                  |
//| 0.0.3 CHANGES (persistent teardown + guards)                     |
//|   - Persistent teardown flag (g_tearingDown): once the close     |
//|     decision is taken, close/delete is retried EVERY tick until  |
//|     TRULY FLAT (no positions AND no pendings).                   |
//|   - Teardown-retry notice is debug level (2).                    |
//|   - OnInit warns when any tier lot is below SYMBOL_VOLUME_MIN    |
//|     (NormalizeLot clamps UP -> real exposure larger than set).   |
//|   - Rejects negative InpDeviationPoints / InpCommissionPerLot.   |
//|   - Recovery log now reports the true pending count even when    |
//|     positions exist.                                             |
//|                                                                  |
//| 0.0.4 CHANGES (pure target-only + teardown flood/restart fixes)  |
//|   - Loss-cut inputs removed; NO stop-loss is ever attached       |
//|     (BuyStop/SellStop always send SL=0.0, TP=0.0).               |
//|   - Teardown flood fix: CloseOnePosition / DeleteOnePending      |
//|     treat ACT_SKIP_WAIT and ACT_ABORT_CYCLE as silent skip.      |
//|     ProcessTearDown backs off InpRetrySeconds via CanTrade()     |
//|     while the market is closed; g_tearingDown stays set so the   |
//|     close-out RESUMES when the market reopens.                   |
//|   - Restart-safe teardown: a per-instance TERMINAL GLOBAL        |
//|     VARIABLE persists the teardown intent across recompile/VPS   |
//|     restart, so an interrupted close-out resumes to flat.        |
//|   - Grid level prices STRICTLY MONOTONIC per side under the      |
//|     stops/freeze clamp (no duplicate/rejected pendings).         |
//|   - Pending placement never re-sends the FULL volume after       |
//|     TRADE_RETCODE_DONE_PARTIAL (10010): any accepted pending is  |
//|     final. PositionClose partial-close handling is kept.         |
//|                                                                  |
//| 0.0.5 CHANGES (basket trailing stop + flood throttle + marker)   |
//|   - Whole-basket price-trailing stop introduced (REPLACED by the |
//|     per-leg model in 0.0.8 - see 0.0.8 CHANGES).                 |
//|   - Definitive teardown flood fix: throttled operational-log     |
//|     chokepoint (LogOp) emits a repeated identical close/delete/  |
//|     place failure line at most once per InpRetrySeconds, and     |
//|     ProcessTearDown applies a NO-PROGRESS BACK-OFF: a teardown   |
//|     attempt that closes/deletes nothing re-arms only after       |
//|     InpRetrySeconds even on an OPEN market, while a teardown     |
//|     that IS making progress still retries next tick.             |
//|   - Marker lifecycle/freshness: the teardown marker is scoped    |
//|     per ACCOUNT+magic+symbol and stamped with the teardown start |
//|     time. On init the marker is DISCARDED when the instance is   |
//|     already flat or the stamp is older than InpMarkerStaleHours, |
//|     so an orphaned marker can never tear down a FRESH grid.      |
//|     OnDeinit deletes the marker on user-initiated removal WHEN   |
//|     FLAT, and keeps it across genuine restarts.                  |
//|                                                                  |
//| 0.0.6 CHANGES (rebalanced tier lots)                             |
//|   - Tier-lot DEFAULTS rebalanced to a flat conservative profile  |
//|     (near 0.01 / mid 0.01 / far 0.01; previously 0.01/0.04/0.07).|
//|   - OnInit sanity WARNING (not an abort) when LotFar > LotNear.  |
//|                                                                  |
//| 0.0.7 CHANGES (true symmetric trailing stop - cut losses)        |
//|   - The whole-basket trail became a true SYMMETRIC trailing stop |
//|     (profit-only gate removed; it could realize a loss). That    |
//|     whole-basket model is REPLACED in 0.0.8 (see below).         |
//|                                                                  |
//| 0.0.8 CHANGES (per-leg trailing scalp + grid recycle)            |
//|   - WHOLE-BASKET symmetric trailing stop REMOVED (armed/dir/     |
//|     extreme/pre-arm-peak state and the "fixed target suspended   |
//|     once armed" interaction are gone). InpTrailArmSteps and      |
//|     InpTrailStepN are REUSED with per-leg meaning: a SIDE        |
//|     becomes trail-active at >= TrailArmSteps OPEN positions      |
//|     (default 2 -> reaches L2); each IN-PROFIT leg on an active   |
//|     side trails TrailStepN grid-steps behind its OWN peak (best  |
//|     favorable close price) and is closed INDIVIDUALLY - always   |
//|     in profit (POSITION_PROFIT > 0 re-checked at the close).     |
//|                                                                  |
//| 0.0.9 CHANGES (real broker-side per-leg trailing SL)             |
//|   - Per-leg trailing no longer market-closes tickets from EA     |
//|     code. It modifies the exact hedging position ticket with     |
//|     PositionModify(ticket, SL, current TP), preserving TP.       |
//|   - Candidate SLs keep the 0.0.8 peak distance, but are placed   |
//|     only on the profit side of entry, snapped side-aware to tick,|
//|     and validated against fresh Bid/Ask plus stops/freeze.       |
//|   - FIXED grid anchor per cycle: level prices are anchored once  |
//|     at cycle start and never re-centered. The population rule    |
//|     (re)places any empty (side, level) slot at its fixed price - |
//|     ONE rule does both the initial 24-order build AND the refill |
//|     after a leg banks. Slots whose fixed price is currently      |
//|     invalid (wrong side of market / inside stops-freeze) are     |
//|     skipped this tick and retried later.                         |
//|   - Side+level encoded in the order comment ("STR B5"/"STR S5")  |
//|     for slot occupancy matching and restart recovery: the anchor |
//|     is re-derived from a leg's price and its comment level       |
//|     (pendings preferred - their open price IS the fixed level    |
//|     unless it was clamped at placement; positions approximate it |
//|     via entry slippage), per-leg peaks re-seed at entry price    |
//|     (favorable excursion before the restart is forgotten).       |
//|   - FULL CLOSE is now NET-based and the ONLY whole-grid          |
//|     teardown: cycleNet = realized this cycle (DEAL_ENTRY_OUT     |
//|     profit+swap+commission+fee since cycle start) + floating     |
//|     (open profit+swap - per-lot fallback commission).            |
//|     cycleNet >= TargetUSD tears everything down; there is NO     |
//|     loss cap - held losers ride until the net target is reached  |
//|     (explicit user choice).                                      |
//|   - While a cycle is active and under target, v0.0.10 deletes    |
//|     only stale/off-grid Straddle pendings before refill; valid   |
//|     fixed-slot pendings are preserved.                           |
//|                                                                  |
//| 0.0.10 CHANGES (strict fixed-slot pricing + boundary teardown)   |
//|   - PlaceStopOrder no longer pushes a fixed slot to ask+distance |
//|     or bid-distance. If the requested fixed level is invalid     |
//|     against fresh Bid/Ask and stops/freeze distance, placement   |
//|     is skipped and retried on later ticks.                       |
//|   - Active cycles tear down through the persistent close/delete  |
//|     path when mid leaves [gridMin, gridMax].                     |
//|   - Stale/off-grid Straddle pending orders are deleted before    |
//|     refill when their level/comment/price no longer matches the  |
//|     fixed grid settings. Positions are not touched by cleanup.   |
//|                                                                  |
//| 0.0.11 CHANGES (profit-funded losing-side partial cleanup)       |
//|   - Optional Profit-Funded Loss Reduction partially closes losing|
//|     tickets using only surplus cycle profit above reserve/cost    |
//|     buffer. This is NOT a hard max-loss or loss cap.             |
//|   - Basket target teardown gives cleanup first use of surplus     |
//|     while losing positions remain, then falls back to normal      |
//|     teardown when cleanup is exhausted or impossible.             |
//|   - Default tier lots restored to 0.01 / 0.03 / 0.06.             |
//|                                                                  |
//| 0.0.12 CHANGES (execution hygiene)                               |
//|   - Pending stops get a final send-time tick refresh and strict   |
//|     fixed-slot validation immediately before BuyStop/SellStop.    |
//|   - Placement accounting counts only accepted CTrade requests.    |
//|   - Visual-tester/program shutdown stops cycle builds and         |
//|     placement loops before OrderOpen can spam disabled requests.  |
//|   - Real trailing SL reselects the exact live ticket immediately  |
//|     before PositionModify to avoid stale same-tick SL closures.   |
//|                                                                  |
//| 0.0.13 CHANGES (no-balance-down rescue hold)                     |
//|   - Unsafe boundary/recovery/teardown close-all decisions now     |
//|     enter persistent rescue hold: delete pendings only, hold      |
//|     positions, keep profitable broker-side trailing SL active,    |
//|     and allow realized-profit-funded cleanup.                     |
//|   - ProcessTearDown has a final cycleNet close-buffer guard       |
//|     immediately before CloseAllPositions().                       |
//|   - Losing cleanup budgets spend realized cycle profit only, not  |
//|     floating basket profit.                                       |
//|                                                                  |
//| 0.0.14 CHANGES (rescue observability + realized bank ladder)      |
//|   - Rescue status logs report balance/equity/margin, trusted      |
//|     marker state, realized/net/floating P/L, bank, and exposure.  |
//|   - Rescue cleanup bank is anchored to account balance at rescue  |
//|     entry; floating profit never funds losing closes.             |
//|   - Losing cleanup uses funded minimum-lot chunks and recomputes  |
//|     realized bank after each accepted partial close.              |
//|   - Optional rescue hedge opens only during rescue, only on       |
//|     hedging accounts, only against net exposure, and only when    |
//|     margin/cooldown/max-hedge guards pass.                        |
//|   - Rescue recenter means the hedge is opened at current market    |
//|     price; the old fixed grid anchor is not moved while rescue     |
//|     positions remain, and the next normal cycle anchors fresh only |
//|     after rescue exits flat.                                      |
//|                                                                  |
//| 0.0.15 CHANGES (Trend Rescue Mode)                               |
//|   - Unsafe grid-boundary breaks can enter Trend Rescue Mode       |
//|     instead of generic two-sided rescue hold.                     |
//|   - Trend rescue blocks normal PopulateGrid(), deletes pendings,  |
//|     keeps old positions open, and opens only trend-side market    |
//|     recovery entries with cooldown/max-entry/margin/OrderCheck    |
//|     gates.                                                        |
//|   - Trend rescue positions use distinct STR TRB/STR TRS comments  |
//|     and remain eligible for the real PositionModify() trailing SL |
//|     path.                                                         |
//|   - Profit-covered reset closes all only when booked profit       |
//|     covers booked loss, current floating loss, and the cost       |
//|     buffer.                                                       |
//|                                                                  |
//| 0.0.16 CHANGES (Trend Rescue sparse OrderCheck handling)          |
//|   - Trend rescue market entries log OrderCheck retcode 0 as       |
//|     sparse tester metadata after local margin gates pass.          |
//|   - Real nonzero OrderCheck reject retcodes still block entries;  |
//|     OrderSend success/failure diagnostics include order/deal ids. |
//|                                                                  |
//| 0.0.17 CHANGES (Adaptive Trend Rescue sizing/harvest)             |
//|   - Trend rescue lots scale toward losing-side frozen exposure    |
//|     while preserving broker margin and OrderCheck/OrderSend gates.|
//|   - Large uncovered gaps reduce entry cooldown/step and increase  |
//|     per-entry harvest targets, without closing losers until the   |
//|     existing profit-covered reset rule is satisfied.              |
//|                                                                  |
//| 0.0.18 CHANGES (Trend Rescue profit-funded loser cleanup)         |
//|   - Trend Rescue harvest still books profitable STR TRB/TRS       |
//|     entries first, then spends only balance booked above the      |
//|     rescue anchor/reserve/buffer to close old losing tickets.     |
//|   - Old-loser cleanup excludes STR TRB/TRS and STR RHG positions, |
//|     prefers the smallest fully funded loser, and can partial-close|
//|     a minimum valid lot when no full ticket is funded.            |
//|                                                                  |
//| 0.0.19 CHANGES (Trend Rescue entry loser cleanup)                 |
//|   - Adds a separate booked-balance-funded cleanup path for losing |
//|     active STR TRB/TRS entries, without using CloseAllPositions(). |
//|   - Blocks new same-direction trend rescue entries while existing|
//|     red STR TRB/TRS entries cannot be funded from booked balance. |
//|                                                                  |
//| 0.0.20 CHANGES (Rolling pair-funded Trend Rescue cleanup)         |
//|   - Removes the red STR TRB/TRS pre-entry freeze; recovery stays  |
//|     active under the normal max-entry/cooldown/step/margin gates. |
//|   - Confirmed short-term reversals can switch future Trend Rescue |
//|     direction without closing existing positions.                 |
//|   - Pair-funded cleanup closes a profitable STR TRB/TRS ticket    |
//|     first, then spends only the freshly booked balance budget on  |
//|     old grid losers first, or red STR TRB/TRS entries second.     |
//|                                                                  |
//| 0.0.21 CHANGES (Recovery entry-gate reset diagnostics)            |
//|   - Confirmed rolling direction switches can reset the trend      |
//|     rescue entry cooldown and continuation-step gate.             |
//|   - Large uncovered coverage gaps promote entry-skip diagnostics  |
//|     from debug to normal level for recovery forensics.            |
//|                                                                  |
//| 0.0.22 CHANGES (Broker money scale and recovery churn guard)      |
//|   - Auto-detects USC/cent-style accounts and scales money inputs. |
//|   - Estimates spread/commission costs before funded cleanup.      |
//|   - Protects fresh active-direction recovery entries from cleanup.|
//|                                                                  |
//| 0.0.23 CHANGES (Trend-rescue entry gate de-stale)                 |
//|   - First same-direction recovery entry ignores stale last price. |
//|   - Rolling direction switches clear the stale price gate.        |
//|   - Continuation-step logs include live same-direction reference. |
//|                                                                  |
//| 0.0.24 CHANGES (Recovery pressure and protected cleanup)          |
//|   - Large unresolved coverage gaps can raise trend-rescue exposure|
//|     pressure while normal max-entry and margin gates still apply. |
//|   - Early continuation-step gates can be bypassed only when the   |
//|     pressure gap is active and the rolling direction is confirmed.|
//|   - Protected-profit cleanup spends only booked account-balance    |
//|     gains above a startup anchor, floor, and buffer on old losers.|
//|   - Noisy recovery skip logs are throttled during fast backtests. |
//|                                                                  |
//| 0.0.25 CHANGES (Loss-floor and money-gap recovery sizing)         |
//|   - Voluntary cleanup closes must respect a strict balance floor. |
//|   - Recovery pressure sizing can respond to uncovered money gaps. |
//|                                                                  |
//| 0.0.26 CHANGES (Cleanup cadence and tester telemetry)             |
//|   - Trend rescue runs harvest before protected cleanup and rechecks|
//|     profit-covered reset after each cleanup stage.                |
//|   - Hot-path skip logs and cleanup diagnostics are throttled by   |
//|     reason to keep tester runs faster without hiding state.       |
//|   - Same-pass rescue snapshot caching removes repeated coverage / |
//|     history scans while preserving exact per-pass behavior.       |
//|   - Old-loser cleanup now favors more blocking exposure and older |
//|     losers; active-direction entry age protection relaxes when    |
//|     the uncovered coverage gap becomes large.                     |
//|                                                                  |
//| 0.0.27 CHANGES (Stuck recovery cleanup)                           |
//|   - Adds a conservative stuck-recovery cleanup path for large      |
//|     trend-rescue coverage gaps when normal cleanup makes no        |
//|     progress. It uses a capped booked-balance/equity cushion       |
//|     budget, prefers partial old-grid loser closes, and never       |
//|     spends below strict account/equity floors.                    |
//|                                                                  |
//| 0.0.28 CHANGES (Log throttles and cleanup speed)                  |
//|   - Cleanup diagnostics and voluntary loss-floor skips are keyed   |
//|     by state buckets so fast tester passes do not flood logs.      |
//|   - Repeated below-min-lot cleanup states are guarded per lane      |
//|     until candidate/budget state changes or the throttle expires.  |
//|   - Trend-rescue processing avoids no-op pending deletes and        |
//|     snapshot invalidation when nothing changed.                    |
//|                                                                  |
//| 0.0.29 CHANGES (Equity pressure and stale cleanup)                |
//|   - Equity-pressure mode slows trend-rescue cadence/step, reduces |
//|     recovery lots, and caps severe-pressure entries without using |
//|     a hard EA stop or close-all.                                  |
//|   - Pair-funded cleanup now carries an orphaned profit reserve if |
//|     a post-profit loser race prevents the paired loser close.      |
//|   - Stale old-grid and red STR TRB/TRS losers can be reduced in   |
//|     capped minimum-lot chunks when age/count/equity-DD thresholds |
//|     are breached.                                                 |
//|                                                                  |
//| 0.0.30 CHANGES (Stale severity and floating pair cleanup)         |
//|   - Stale cleanup can trigger from large age-qualified losses, not|
//|     only from stale ticket count.                                 |
//|   - Orphaned pair reserves expire quickly, accumulate safely, and |
//|     no longer block later cleanup/recovery lanes for hours.       |
//|   - Favorable STR TRB/TRS floating profit can be booked to fund   |
//|     opposite stale loser chunks while preserving floor/margin gates.|
//|                                                                  |
//| 0.0.31 CHANGES (Adaptive recovery architecture)                   |
//|   - Price-distance inputs can scale by current XAU price and ATR  |
//|     so recovery/grid spacing adapts across large price regimes.   |
//|   - Trend-rescue entries must prove expected coverage or cleanup  |
//|     funding impact before OrderCheck/OrderSend, with first-entry  |
//|     and no-position exceptions kept explicit.                     |
//|   - Stale backpressure blocks entries only when stale cleanup is  |
//|     actually actionable under budget/min-lot/floor constraints.   |
//|   - Opposite STR exposure is capped unless the new entry reduces  |
//|     stale old exposure or passes the impact guard.                |
//|                                                                  |
//| 0.0.32 CHANGES (Tester low-log performance mode)                 |
//|   - Adds tester/optimization-aware low-log mode for faster every- |
//|     tick backtests without changing trading decisions.            |
//|   - Repetitive skip, stale-status, retcode-zero, and successful   |
//|     trailing-modify logs emit only on state change/summary gates. |
//|                                                                  |
//| 0.0.33 CHANGES (Log-gate parity and hard exposure backstop)       |
//|   - Rescue-hedge OrderCheck retcode-zero skip log now routes       |
//|     through the same low-log suppression as the trend-rescue path. |
//|   - Adds a hard opposite-STR-exposure ceiling that neither the     |
//|     impact-proven nor net-reduce soft escapes can bypass.          |
//|                                                                  |
//| 0.0.34 CHANGES (Structural drawdown control)                      |
//|   - Makes the rescue hedge reachable during active trend rescue    |
//|     (relaxes the two g_rescueHolding gates to also accept trend-    |
//|     rescue state and calls harvest+open from ProcessTrendRescue);   |
//|     net exposure, sizing, sign, cooldown and margin logic unchanged.|
//|   - Fixes the teardown cancel-race: ProcessTearDown now deletes     |
//|     pendings BEFORE closing positions, then re-closes a same-tick   |
//|     residual fill only while the cycle is still net-safe.           |
//|                                                                  |
//| 0.0.35 CHANGES (State-agnostic net-exposure hedge)                |
//|   - Rescue hedge is now reachable in a NORMAL CYCLE (not just       |
//|     rescue-hold / trend-rescue) whenever there is real net exposure |
//|     AND floating loss past InpRescueHedgeTriggerLossUSD, so an       |
//|     orphan/stale net-long (or net-short) loser pile gets hedged and |
//|     equity drawdown stops growing WITHOUT realizing the loss.       |
//|     Done via a new StuckExposureHedgeEligible() disjunct OR'd into   |
//|     RescueHedgeStateActive(), plus one guarded harvest+open call in  |
//|     the normal-cycle tail of ManageCycle (reuses existing bid/ask).  |
//|     All per-call self-gates (account-hedging, cooldown, max-hedges,  |
//|     loss-trigger, net-lots, margin) and sizing/sign are UNCHANGED;   |
//|     reuses InpUseRescueHedge + existing hedge inputs (NO new input). |
//|   - Harvest reachability broadened: TryHarvestRescueHedges also runs |
//|     when CountRescueHedges()>0 so a hedge that fully neutralizes net  |
//|     exposure (gate flips false) is never stranded un-harvestable.    |
//|                                                                  |
//| 0.0.36 CHANGES (Ungate trend-rescue hedge from pending state)     |
//|   - In ProcessTrendRescue the net-sized rescue-hedge open was       |
//|     gated behind 'if(pendingsAfterDelete == 0)', but trend rescue   |
//|     keeps continuation pendings so pendingsAfterDelete>0 nearly      |
//|     every tick -> TryOpenRescueHedge was skipped forever and the    |
//|     STR RHG hedge NEVER fired, leaving the net-long orphan loser     |
//|     pile riding unbounded equity drawdown. The rescue hedge is an    |
//|     independent net-exposure position (sized from NetExposureLots,   |
//|     self-gated on InpUseRescueHedge/IsHedgingAccount/cooldown/        |
//|     max-hedges/loss-trigger/margin) - pending-order state is         |
//|     irrelevant to it. Removed ONLY that pendings gate so the hedge    |
//|     fires every eligible tick. The trend-rescue ENTRY keeps its      |
//|     pendings gate; sizing/sign/self-gates all UNCHANGED.            |
//|                                                                  |
//| 0.0.37 CHANGES (recovery-factor consolidation: net-sized sticky      |
//|                 hedge + account equity circuit breaker)             |
//|   - C1: Fix the rescue-HOLD hedge reachability regression. The dead  |
//|     'if(pendingsAfterDelete==0)' gate (removed in 0.0.36 from        |
//|     ProcessTrendRescue and the normal tail but MISSED in             |
//|     ProcessRescueHold) is removed so TryOpenRescueHedge is           |
//|     unconditional inside the bid/ask guard - hedge reachability is   |
//|     now uniform across all three states. Pure correctness, no new    |
//|     behavior; the hedge self-gates internally.                      |
//|   - C2: Stop sabotaging the +target bank. The 'delaying full         |
//|     teardown' early-return in the net>=target branch is removed so   |
//|     reaching target ALWAYS banks (monotonic profit, faster, shorter  |
//|     cleanup cycles). Leftover loser cleanup remains in its dedicated  |
//|     lanes and section-3.                                            |
//|   - C3: Net-size the rescue hedge. The fixed 0.01-lot token request  |
//|     becomes max(InpRescueHedgeLot, |net|*InpRescueHedgeCoverageFrac) |
//|     clamped to net via NormalizeCloseLotsDown. New input             |
//|     InpRescueHedgeCoverageFrac defaults 0.0 -> bit-for-bit 0.0.36    |
//|     (max(0.01,|net|*0)=0.01 token). Hedge open is factored into a    |
//|     shared OpenNetHedge(bid,ask,coverFrac,reason) helper so C3/C5    |
//|     use ONE OrderSend/guard path.                                   |
//|   - C4: Sticky hedge harvest under drawdown. A profitable hedge is   |
//|     HELD (harvest skipped) while equity DD >= InpRescueHedgeHoldDDUSD |
//|     and the hold age < InpRescueHedgeMaxHoldHours, so the protective |
//|     offset stays on through the crash instead of being harvested at  |
//|     +InpRescueHedgeHarvestUSD and re-exposing the pile. New inputs   |
//|     default 0.0 -> legacy harvest behavior. Only DEFERS a close;     |
//|     never opens exposure, never realizes a loss.                    |
//|   - C5: Account-level equity-DD circuit breaker. New                 |
//|     EquityHedgeBackstop(bid,ask) runs as the LAST normal-cycle tail  |
//|     action: when equity DD >= InpEquityHardFlattenDDUSD it ARMS and  |
//|     opens a FULL-net hedge (OpenNetHedge coverFrac=1.0), bounding    |
//|     maxDD WITHOUT realizing loss; two-threshold hysteresis           |
//|     (InpEquityHardFlattenReleaseDDUSD) disarms it. Reuses the SAME   |
//|     g_lastRescueHedgeTime cooldown stamp as the rescue hedge so the  |
//|     two opens are mutually exclusive per tick. New persisted         |
//|     g_peakEquity (GV pattern, restart-robust) is monitoring only;    |
//|     arm/release use absolute EquityFloatingDrawdown(). Defaults 0.0  |
//|     -> backstop OFF. NO close-all, NO refuse-to-trade, NO entry      |
//|     suppression - the grid keeps banking around the frozen book.     |
//|   - Every new input defaults to a value reproducing 0.0.36           |
//|     bit-for-bit, so existing dd_control/dd_hedge/dd_hedge_fast        |
//|     setfiles load unchanged until the user tunes.                    |
//+------------------------------------------------------------------+
//| 0.0.38 CHANGES (account-level basket take-profit, no-loss invariant)|
//|   - NEW input InpBasketTakeProfitUSD (default 0.0 = OFF). When > 0,  |
//|     the EA banks the ENTIRE book (orphans + grid + STR TRB/TRS + any |
//|     RHG hedge) the instant the TOTAL floating P/L of the whole book  |
//|     (the swap+commission-adjusted OpenFloatingPL net) first reaches  |
//|     >= +MoneyInput(InpBasketTakeProfitUSD). State-agnostic: fires in |
//|     normal-cycle / trend-rescue / rescue-hold alike.                 |
//|   - STRICT NO-LOSS INVARIANT: the gate fires ONLY when total         |
//|     floating >= +threshold with threshold strictly > 0, so the path  |
//|     can NEVER realize a loss. NO per-leg cutting, NO close-at-loss,   |
//|     NO refuse-to-trade. Routed through the existing BeginTearDown /  |
//|     ProcessTearDown clean-reset path.                                |
//|   - New helper CheckBasketTakeProfit() runs near the top of          |
//|     ManageCycle() (after the g_tearingDown early-return) so it never |
//|     fights an in-progress teardown; an explicit 'if(g_tearingDown)   |
//|     return;' after the call keeps the rest of the cycle from running |
//|     against an already-closing book. Re-entrancy / same-tick double  |
//|     close guarded by the g_tearingDown flag.                         |
//|   - RESUME-PATH HARDENING (closes the only realizable-loss window):  |
//|     a basket-initiated teardown is TAGGED (g_basketTearDown, also    |
//|     persisted in a per-instance GlobalVariable so a VPS restart      |
//|     mid-teardown resumes under the basket guard, not the weaker      |
//|     generic CycleNet>=0 guard). While the tag is set, ProcessTearDown |
//|     RE-READS OpenFloatingPL fresh and HOLDS (does NOT close, does    |
//|     NOT realize) unless floating is STILL >= +threshold. This shuts  |
//|     the market-closed-resume / partial-close-retry drift that could  |
//|     otherwise let the weaker guard bank a negative-floating close.   |
//|   - Untrusted-cycle-start divert (EnsureTrustedCycleStartForClose -> |
//|     EnterRescueHold) is intentionally LEFT IN PLACE: basket-TP simply |
//|     DEFERS banking a green book until the anchor is re-trusted; it   |
//|     is never bypassed (bypassing it would reopen a dirty-grid risk). |
//|   - InpBasketTakeProfitUSD default 0.0 reproduces 0.0.37 bit-for-bit |
//|     (helper returns on the first guard; zero added trades / logs),   |
//|     so existing setfiles load unchanged until the user tunes.        |
//|                                                                  |
//| 0.0.39 CHANGES (bounded averaging-down recovery engine)          |
//|   - NEW self-contained "Basket Averaging" engine: a single new      |
//|     function TryBasketAveragingEntry(bid,ask) wired ONCE into the    |
//|     normal active-cycle tail of ManageCycle. It OPENS ONLY market    |
//|     adds on the BURIED side (BUY to average buried longs LOWER /     |
//|     SELL to average buried shorts HIGHER) tagged "STR AVB"/"STR AVS",|
//|     lowering/raising the basket break-even so a small favorable      |
//|     bounce floats the whole book green and the EXISTING basket-TP    |
//|     (CheckBasketTakeProfit) banks it. It NEVER closes, NEVER cuts,   |
//|     NEVER realizes a loss - the green basket-TP is the sole close.   |
//|   - THIS IS A MARTINGALE: the hard caps BOUND the worst-case loss,   |
//|     they do NOT prevent it. In a one-way move that never bounces,    |
//|     all InpAvgMaxLots of added volume sit as additional floating     |
//|     drawdown ON TOP of the original buried pile until a bounce banks |
//|     the book, the equity backstop/rescue-hold takes the tail, or     |
//|     margin runs out. Adding into a crash DELIBERATELY increases DD   |
//|     before any recovery. Size InpAvgMaxLots / InpAvgMinMarginLevelPct|
//|     / InpEquityHardFlattenDDUSD to survive that bounded DD.          |
//|   - TWO INDEPENDENT HARD CAPS, both recomputed LIVE from positions   |
//|     (CountAveragingEntries / AveragingTotalLots) so a restart/       |
//|     recompile cannot reset them: a COUNT cap (InpAvgMaxEntries) and  |
//|     a TOTAL-LOTS cap (InpAvgMaxLots, the absolute blow-up bound).    |
//|     Both are tested next-add-inclusive BEFORE the add AND re-tested  |
//|     INSIDE the OrderSend retry loop before each send, so a slow/     |
//|     partial fill can never breach InpAvgMaxLots.                     |
//|   - PROJECTED MARGIN SAFETY (BasketAveragingMarginOk, cloned from    |
//|     TrendRescueMarginOk): margin-level floor InpAvgMinMarginLevelPct |
//|     (default 500%, stricter than the 300% rescue/trend gates) PLUS   |
//|     OrderCalcMargin vs ACCOUNT_MARGIN_FREE minus the cost buffer,    |
//|     re-checked inside the send loop so a margin move cannot slip an  |
//|     unsafe add through.                                              |
//|   - MUTUAL EXCLUSION with the ENTIRE hedge/rescue/backstop family:   |
//|     the engine returns false if g_trendRescueActive || g_rescueHold  |
//|     || g_tearingDown || g_equityBackstopArmed || RescueHedgeState    |
//|     Active() || StuckExposureHedgeEligible(). It is also called      |
//|     FIRST in the tail and shares the g_lastRescueHedgeTime one-open- |
//|     per-tick stamp with the rescue hedge / equity backstop, so at    |
//|     most ONE market order opens per tick across all engines (no      |
//|     opposing same-tick orders, no double-stack margin spike).        |
//|   - Buried side is derived from grid + STR AV legs ONLY (STR RHG     |
//|     hedge and STR TR legs excluded) so an open hedge cannot          |
//|     misdirect the add, cross-checked by the buried-side floating     |
//|     <= -InpAvgTriggerLossUSD trigger; balanced/flat book -> no add.  |
//|   - STR AV* legs are EXCLUDED from every realized-loss loser/stale   |
//|     cleanup lane AND from per-leg trailing, so an intentionally-      |
//|     underwater add can never be closed at a loss or fragmented.      |
//|   - HALF-CONFIGURED SETFILE = OFF: if master ON but any of           |
//|     InpAvgTriggerLossUSD / InpAvgMaxEntries / InpAvgMaxLots <= 0 the  |
//|     engine makes no adds (each zero sub-gate disables it), so it can |
//|     never run unbounded.                                             |
//|   - ALL new inputs default OFF/0 so InpUseBasketAveraging=false is    |
//|     bit-for-bit 0.0.38 (zero added trades/logs) and every existing   |
//|     setfile loads with no new required fields.                       |
//|                                                                  |
//| 0.0.40 CHANGES (behavior-identical performance pass - ZERO trade   |
//|   drift; same trades, same balance/equity/drawdown, same Experts   |
//|   log lines as 0.0.39)                                             |
//|   - O1 PER-TICK AGGREGATE CACHE: a single global g_bookDirty flag   |
//|     (init true) + a BookAggregates struct + RecomputeBookAggregates |
//|     collapse the ~13 whole-book scan getters (CountMyPositions,     |
//|     CountMyPendings, OpenFloatingPL, NetExposureLots,              |
//|     CountRescueHedges, CountTrendRescueEntries[/ForDirection],      |
//|     CountAveragingEntries, AveragingTotalLots,                     |
//|     AveragingCoreExposureLots, AveragingCoreSideFloating,          |
//|     TrendRescueExposureLotsForSide, StaleOldGridExposureLotsForSide)|
//|     into AT MOST ONE PositionsTotal() + ONE OrdersTotal() pass per  |
//|     dirty epoch. The filter/accumulation logic is copied VERBATIM   |
//|     from the 0.0.39 loops so every cached read equals the value a   |
//|     fresh scan would have produced at that program point.          |
//|   - CACHE INVALIDATION (provably exhaustive - the EA is single-     |
//|     threaded with NO OnTrade/OnTradeTransaction handler, so the     |
//|     book mutates ONLY at the 11 synchronous trade-op sites):        |
//|     g_bookDirty=true is set (a) at the FIRST line of ManageCycle()  |
//|     (covers both OnTick and OnTimer -> a new tick means price/time  |
//|     moved, so floating/PROFIT/SWAP and stale-age members are stale) |
//|     and (b) as the FIRST statement of EVERY successful trade-op     |
//|     branch BEFORE any in-branch aggregate read: PlaceStopOrder      |
//|     BuyStop/SellStop, CloseOnePosition PositionClose,              |
//|     CloseCleanupPartial / CloseTrendRescueEntryCleanupPartial       |
//|     PositionClosePartial, CloseTrendRescuePairProfitTicket /        |
//|     TryHarvestRescueHedges / CloseTrendRescueEntryIfProfitable      |
//|     PositionClose, TryOpenRescueHedge / TryBasketAveragingEntry /   |
//|     TryOpenTrendRescueEntry OrderSend, DeleteOnePending OrderDelete,|
//|     plus the SL-only PositionModify (no aggregate impact - set for  |
//|     a uniform "every trade op dirties" rule, harmless extra        |
//|     recompute). This is a SUPERSET of the existing                 |
//|     InvalidateTrendRescueSnapshot() hooks, which are NOT a          |
//|     sufficient invalidation point.                                 |
//|   - CycleRealized() stays UN-CACHED (it walks DEAL HISTORY via      |
//|     HistorySelect and is call-order sensitive); CycleNet() still    |
//|     calls live CycleRealized() and the cached OpenFloatingPL().     |
//|     CountLosingPositions() stays UN-CACHED (not on the hot path).   |
//|   - O2 GATE-BEFORE-FORMAT: hot per-tick/in-loop Log(level,          |
//|     StringFormat(...)) sites are wrapped as if(level<=             |
//|     EffectiveLogLevel()) Log(level,StringFormat(...)); -            |
//|     EffectiveLogLevel() just returns InpLogLevel (no side effects)  |
//|     so the emitted text is byte-identical when the gate passes and  |
//|     absent when it fails - exactly as Log() already decides, only   |
//|     the wasted StringFormat build is removed. LogOp / ShouldEmitLog |
//|     sites are LEFT ALONE (LogOp is a dedup throttle, not a level    |
//|     gate; it must keep building its string).                       |
//|   - O3 PERSIST iATR HANDLE: ATRPriceDistance() now reuses a single  |
//|     global g_atrHandle (created lazily, released once in OnDeinit)  |
//|     instead of creating + IndicatorRelease-ing a fresh handle every |
//|     call. CopyBuffer(g_atrHandle,0,0,1,buffer) STILL runs every     |
//|     call so the RETURNED forming-bar ATR is bit-identical; only the |
//|     handle create/destroy churn is removed. The iHigh/iLow/iClose   |
//|     fallback loop and the buffer[0]>0.0 guard are preserved.       |
//|   - DROPPED (not provably identical, per critique): per-bar ATR     |
//|     VALUE cache (CopyBuffer index 0 is the forming bar, varies      |
//|     intra-bar - caching would change grid step / price scale /      |
//|     expected-move and thus trade prices) and the tester diagnostic /|
//|     suppressed-only-work throttle (interleaved with throttle-state  |
//|     bookkeeping that can feed later emit decisions - not provably   |
//|     log-only; its safe slice is already captured by O2).           |
//|   - NO new inputs, NO threshold/timing/default changes. This build  |
//|     is engineered to produce a byte-identical Deals/Orders/HTML     |
//|     report and identical Experts log lines vs 0.0.39 at the same    |
//|     InpLogLevel, only faster.                                       |
//|                                                                  |
//| 0.0.41 CHANGES (equity-backstop reachability + no-loss hardening - |
//|   ALL three fixes are NO-OPs at the relevant default inputs, so a   |
//|   DEFAULT-config run is bit-for-bit 0.0.40; existing setfiles load  |
//|   unchanged; NO new inputs - reuses InpEquityHardFlatten* / Inp     |
//|   BasketTakeProfitUSD / InpUseBasketAveraging)                      |
//|   - FIX A (equity backstop reachable in ALL states): the equity-DD  |
//|     circuit breaker (EquityHedgeBackstop) was called from EXACTLY   |
//|     ONE site - the normal-cycle tail - so it NEVER ran during       |
//|     rescue-hold or trend-rescue (those branches return from         |
//|     ManageCycle before the tail), letting equity DD overshoot       |
//|     InpEquityHardFlattenDDUSD uncapped in exactly the states that   |
//|     need it most. NOW one STATE-AGNOSTIC call sits AFTER the        |
//|     teardown + basket-TP early-returns (never hedges into an in-    |
//|     progress net-close) but BEFORE the rescue-hold / trend-rescue / |
//|     normal-cycle branches, so the backstop fires in every state. It |
//|     fetches fresh bid/ask via RefreshCurrentPrices and self-gates   |
//|     on InpEquityHardFlattenDDUSD<=0 (no-op at the 0.0.40 default 0).|
//|     The OLD redundant tail call is REMOVED (the new top call now    |
//|     covers the normal cycle too) so there is exactly ONE backstop   |
//|     call per tick in every state. It is an OPEN (OpenNetHedge,      |
//|     coverFrac=1.0), never a close, so it NEVER realizes a loss. The |
//|     shared g_lastRescueHedgeTime one-open-per-tick stamp prevents a |
//|     same-tick double-open with the rescue hedge / averaging add.    |
//|     NOTE: when the backstop is ON (InpEquityHardFlattenDDUSD>0) AND  |
//|     averaging / rescue-hedge are simultaneously eligible in a       |
//|     NORMAL cycle, the backstop now claims the one-open-per-tick      |
//|     budget FIRST (intended circuit-breaker priority) - this ON+ON   |
//|     case is NOT bit-for-bit 0.0.40; parity holds at the FlattenDD=0 |
//|     default. Backstop no-double-open is hardened: its effective     |
//|     cooldown is now floored at >= 1s (was only capped at 30) AND    |
//|     OnInit emits a non-fatal WARN if InpRescueHedgeCooldownSec==0    |
//|     with FlattenDD>0 + InpUseRescueHedge + InpRescueMaxHedges>1.     |
//|   - FIX B (mid-loop no-loss guard on the basket close): CloseAll     |
//|     Positions gained two DEFAULT-valued params (basketGuard=false,  |
//|     basketThreshold=0.0). On the basket-TP teardown path ONLY,      |
//|     it re-reads FRESH whole-book floating BETWEEN ticket closes and  |
//|     ABORTS (leaving remaining legs OPEN, g_tearingDown stays set,    |
//|     re-arms next tick) if floating drops below +threshold mid-loop,  |
//|     closing the only LIVE adverse-spike window that could bank a red |
//|     tail leg below the basket promise. The fresh re-read re-walks    |
//|     only after a SUCCESSFUL close (g_bookDirty set by CloseOne       |
//|     Position); a failed close banks nothing so the invariant holds.  |
//|     Every non-basket caller uses the no-arg default = bit-for-bit    |
//|     0.0.40. In the tester (frozen single tick) floating only         |
//|     improves as red legs vanish, so the guard never trips.          |
//|   - FIX C (averaging persistence + deepest-loss buried side):        |
//|     (C1) the two averaging gate globals (g_basketAvgLastEntryPrice  |
//|     + g_lastBasketAvgEntryTime) now PERSIST to per-instance terminal |
//|     globals (BAPVar/BATVar) on every accepted add and RELOAD in      |
//|     OnInit (gated on InpUseBasketAveraging so default-OFF startup    |
//|     reads no GV), so a recompile/restart mid-episode keeps the       |
//|     adverse-step + cooldown gates instead of treating the next add   |
//|     as a first-ever add; the GVs are CLEARED on cycle-flat (teardown |
//|     complete + rescue-hold flat) so a new episode never inherits a   |
//|     stale reference. (C2) the buried side is now chosen by the MOST- |
//|     NEGATIVE AveragingCoreSideFloating (deepest USD loss) instead of |
//|     the larger-lot side, so the add targets the truly buried pile on |
//|     a lot-imbalanced book. All of FIX C is behind InpUseBasketAvera  |
//|     ging (default OFF = bit-for-bit 0.0.40).                         |
//|   - DEFERRED (out of scope this batch): rank 9 cycle-scoped net-     |
//|     target / residual-orphan routing.                               |
//+------------------------------------------------------------------+
#property copyright   "Straddle"
#property version     "1.136" // MQL5 Market-compatible encoding of product release Straddle 1.1.36.
#property description "Straddle v1.1.36 - daily % path fix; equity-only multi-day pause; 1.1.34 parity."
#property description "Auto price scaling and daily equity limits."
#property description "Hedging-account dual-sided grid."

#resource "StraddleLogo85.bmp"

#include <Trade/Trade.mqh>
#include <Trade/SymbolInfo.mqh>

enum ENUM_PRICE_SCALE_MODE
  {
   PRICE_SCALE_BLEND_PERCENT_ATR = 0,
   PRICE_SCALE_PRICE_PERCENT_ONLY = 1,
   PRICE_SCALE_ATR_ONLY = 2
  };

enum ENUM_LOT_MODE
  {
   LOT_MODE_FIXED = 0,
   LOT_MODE_AUTO_BALANCE = 1
  };

// After HARD daily loss limit: how many server days to stay flat / no new trades.
enum ENUM_DAILY_LOSS_NO_TRADE_DAYS
  {
   DAILY_LOSS_NO_TRADE_1_DAY  = 1, // 1 day
   DAILY_LOSS_NO_TRADE_2_DAYS = 2, // 2 days
   DAILY_LOSS_NO_TRADE_3_DAYS = 3, // 3 days
   DAILY_LOSS_NO_TRADE_4_DAYS = 4  // 4 days
  };

/*
  NON-EXECUTABLE - Legacy input migration map
  bool   InpUseOrphanRelease=false                  - Orphan unsupported in v1.1.0
  double InpOrphanReleaseDDUSD=0.0                  - Orphan unsupported in v1.1.0
  int    InpOrphanReleaseHours=24                   - Orphan unsupported in v1.1.0
  int    InpMaxOrphanLots=0                         - Orphan unsupported in v1.1.0
  int    InpOrphanStaleReleaseHours=0               - Orphan unsupported in v1.1.0
  double InpOrphanStaleMaxNetUSD=5.0                - Orphan unsupported in v1.1.0
  int    InpOrphanStaleReleaseCooldownHours=24      - Orphan unsupported in v1.1.0
  bool   InpAnimateStraddleChartUI=false             - dead animation input removed
*/

//======= PUBLIC INPUTS (Inputs tab) =================================
input group "1. Account"
input long   InpMagic            = 26011001;   // Magic number
input int    InpLogLevel         = 0;          // 0=errors+cycle banks only, 1=state events, 2=debug

input group "2. Grid and Lots"
input int    InpGridLevels       = 30;         // 1.1.17: levels per side (safer than 50-60)
input double InpGridStepUSD      = 2.0;        // reference spacing at InpPriceScaleReferencePrice
input bool   InpUsePriceProportionalGrid = true; // scale spacing by current price
input double InpPriceScaleReferencePrice = 5000.0; // price where InpGridStepUSD applies
input bool   InpKeepGridPendings = true;       // keep buy/sell stops on chart
input double InpLotNear          = 0.01;       // Near-tier lots
input double InpLotMid           = 0.02;       // Mid-tier lots
input double InpLotFar           = 0.03;       // Far-tier lots

input group "2b Lot Mode"
input ENUM_LOT_MODE InpLotMode = LOT_MODE_FIXED; // exactly one mode: fixed tier lots or auto balance lots
input double InpAutoLotBaseBalanceUSD = 2000.0;  // reference balance for auto lots
input double InpAutoLotNear = 0.01;              // at base balance: near-tier lots
input double InpAutoLotMid  = 0.05;              // at base balance: mid-tier lots
input double InpAutoLotFar  = 0.10;              // at base balance: far-tier lots

input group "3. Profit Targets"
input double InpTargetUSD            = 30.0;   // fixed-mode cycle net bank target
input double InpBasketTakeProfitUSD  = 0.0;    // whole-book green bank (0=off)

input group "3b Daily Limits"
input bool   InpUseDailyLimits       = true;   // true=1.1.22 hard daily stop, false=disable daily DD
input bool   InpUseOpenBookHardStop  = true;   // independent EA floating-P/L $ stop
input bool   InpDailyLimitsUsePercent = true;  // true=use AutoDaily*Percent of day-start equity (15%/20% etc). false=USD $ caps only (unless lot mode is Auto)
input double InpDailyProfitLimitUSD  = 150.0;  // day equity profit cap in USD (used when percent path is OFF)
input double InpDailyLossLimitUSD    = 400.0;  // HARD day equity loss cap in USD (used when percent path is OFF)
input double InpAutoCycleTargetPercent = 1.50; // auto-mode cycle target as % of balance
input double InpAutoDailyProfitPercent = 7.50; // daily profit cap as % of day-start equity (auto lots, or when InpDailyLimitsUsePercent)
input double InpAutoDailyLossPercent = 20.00;  // HARD daily loss cap as % of day-start equity (auto lots, or when InpDailyLimitsUsePercent)
input bool   InpDailyLossNextDayLot2x = true;  // after loss: next day lots+cycle target x2
input ENUM_DAILY_LOSS_NO_TRADE_DAYS InpDailyLossNoTradeDays = DAILY_LOSS_NO_TRADE_1_DAY; // Do not trade for this many days after daily LOSS limit (1-4)

input group "4. Trend (detection + optional one-side)"
input bool   InpUseTrendOneSideGrid    = false; // both sides always
input bool   InpUseTrendPause          = false; // allow new cycle when flat
input int    InpTrendPauseLookbackBars = 6;     // bars for trend move
input double InpTrendPauseMoveUSD      = 30.0;  // USD move to call it a trend
input bool   InpUseTrendDoubleLot      = true;  // 2x with-trend grid lots when deep DD + trend
input double InpTrendDoubleLotLossUSD  = 800.0; // 1.1.17: do NOT use 200 (logs: float -$14k)

input group "5. Trailing (per-leg only; whole basket waits for TargetUSD)"
input bool   InpUseTrailing            = true;  // trail each IN-PROFIT leg only
input int    InpTrailArmSteps          = 5;     // profit grids to ARM trail (price, not #positions)
input int    InpTrailStepN             = 2;     // trail distance after arm (2 or 1)
input bool   InpTrailOnlyIfEquityGreen = false; // OFF: trail even if equity < balance
input double InpTrailMinEquityLeadUSD  = 0.0;   // only if equity-green gate ON

input group "6. Extra engines (OFF = pure straddle; leave off unless you know)"
input bool   InpUseBasketRecovery      = false; // OFF
input double InpBasketRecoveryTargetUSD = 0.0;
input int    InpBasketRecoveryMaxOrders = 20;
input double InpBasketRecoveryLot      = 0.0;
input double InpBasketRecoveryStepUSD  = 0.0;
input bool   InpFastEquityRecovery     = false; // OFF
input double InpEquityRecoveryDDUSD    = 80.0;
input bool   InpNeverRealizeLoss       = true;  // hold losers until cycle net target
input int    InpFloatSleeperMaxHours   = 24;
input bool   InpUseTrendRescueMode     = false; // OFF
input bool   InpUseFloatReanchor       = false; // OFF
input bool   InpUseRescueHedge         = false;
input bool   InpUseProfitFundedCleanup = false; // OFF

input group "7. Chart"
input bool   InpShowStraddleChartUI = true;     // chart UI enabled by default; set false for tester speed

//======= INTERNAL DEFAULTS (not shown in Inputs tab) =======
// 1.1.2: demoted advanced knobs. Edit source only if you need them.
// Values match 1.1.1 unless noted as a 1.1.2 safe override.

//--- Identification / tester ----------------------------------------
const int    InpTesterSecPerBar  = 0;

//--- Grid tier counts ------------------------------------------------
// 1.1.25: tier boundaries are derived from the active level count so a
// 100-level grid is split 33/33/34 and a 30-level grid is split 10/10/10.
int TierNearCount()
  {
   int levels = EffectiveGridLevels();
   if(levels < 1)
      levels = 1;
   int near = levels / 3;
   if(near < 1)
      near = 1;
   return near;
  }

int TierMidCount()
  {
   int levels = EffectiveGridLevels();
   if(levels < 1)
      levels = 1;
   int near = TierNearCount();
   int mid = (levels - near) / 2;
   if(mid < 1 && levels > 1)
      mid = 1;
   return mid;
  }
const bool   InpTrendOneSideBlockHedge = true;

//--- 1.1.14/1.1.15 TREND-2X (INTERNAL — not on Inputs tab) ----------
// 1.1.15: cleanup+fast-bank OFF — realizing losers dropped balance in logs.
const double InpTrend2xMoveUSD       = 15.0;  // $ move over lookback bars to arm 2x
const bool   InpTrend2xProfitCleanup = false; // OFF: never force red closes on recovery
const bool   InpTrend2xFastBank      = false; // OFF: wait full TargetUSD (no $5 forced bank)
const double InpTrend2xFastBankUSD   = 5.0;   // unused while FastBank=false

//--- Adaptive price scale -------------------------------------------
const bool   InpUseAdaptivePriceScale = true;
const int    InpPriceScaleMode = PRICE_SCALE_PRICE_PERCENT_ONLY;
const double InpPriceScalePercentWeight = 0.50;
const double InpPriceScaleATRWeight = 0.50;
const int    InpPriceScaleATRPeriod = 14;
const ENUM_TIMEFRAMES InpPriceScaleATRTimeframe = PERIOD_M15;
const double InpPriceScaleReferenceATR = 8.0;
const double InpPriceScaleMin = 0.10;
const double InpPriceScaleMax = 10.0;

//--- Basket costs / cleanup buffers ---------------------------------
const int    InpPauseSeconds     = 2;          // 1.1.4: faster rebuild after bank
const double InpCommissionPerLot = 0.0;
const double InpMoneyScaleOverride = 0.0;
const bool   InpUseAutoSpreadCost = true;
const bool   InpUseAutoCommissionEstimate = true;
const double InpProfitReserveUSD = 1.0;        // 1.1.8: almost all surplus funds loser cleanup
const double InpCleanupCostBufferUSD = 1.0;
const int    InpCleanupMinLosingPositions = 1;

//--- Float re-anchor (1.1.4 dense-grid: early + uncapped by default) -
const double InpFloatReanchorDDUSD       = 25.0;   // 1.1.8: float sooner
const int    InpFloatReanchorHours       = 0;      // 1.1.8: no multi-hour wait when stuck
const int    InpFloatStaleReanchorHours  = 2;
const double InpFloatStaleMaxNetUSD      = 15.0;
const int    InpFloatStaleCooldownHours  = 4;
const double InpFloatCloseBufferUSD      = 0.0;
const double InpMaxFloatLots             = 0.0;    // uncapped

//--- Rescue hedge / equity backstop ---------------------------------
const int    InpRescueStatusLogSeconds = 60;
const double InpRescueHedgeLot = 0.01;
const int    InpRescueHedgeCooldownSec = 300;
const int    InpRescueMaxHedges = 1;
const double InpRescueHedgeMinMarginLevelPct = 300.0;
const double InpRescueHedgeTriggerLossUSD = 50.0;
const double InpRescueHedgeHarvestUSD = 5.0;
const double InpRescueHedgeCoverageFrac = 0.0;
const double InpRescueHedgeHoldDDUSD = 0.0;
const double InpRescueHedgeMaxHoldHours = 0.0;
const double InpEquityHardFlattenDDUSD = 0.0;        // OFF
const double InpEquityHardFlattenReleaseDDUSD = 0.0;

//--- Trend rescue entry / sizing ------------------------------------
const double InpTrendRescueLot = 0.02;         // base recovery lot
const double InpTrendRescueStepUSD = 1.5;
const int    InpTrendRescueMaxEntries = 12;    // 1.1.7: fewer stacked recovery entries
const int    InpTrendRescueCooldownSec = 60;    // 1.1.7: slightly slower fire rate
const double InpTrendRescueMinMarginLevelPct = 250.0;
const double InpTrendRescueProfitTargetUSD = 4.0;
const bool   InpUseAdaptiveTrendRescueSizing = true;
const double InpTrendRescueExposureRatio = 1.00;
const double InpTrendRescueMaxEntryLot = 0.03; // 1.1.7: HARD CAP (was 0.08 — Jan19 equity hole)
const double InpTrendRescueMoneyGapLotStepUSD = 100.0;

//--- Trend rescue pressure / cadence --------------------------------
const int    InpTrendRescuePressureMaxEntries = 12; // 1.1.7: match entry cap
const int    InpTrendRescueTotalSafetyMaxEntries = 18;
const bool   InpUseTrendRescueCoveragePressure = true;
const double InpTrendRescuePressureGapUSD = 120.0;
const double InpTrendRescuePressureExposureRatio = 1.60;
const int    InpTrendRescuePressureMinExtraEntries = 2;
const bool   InpUseTrendRescueContinuationPressureOverride = true;
const int    InpTrendRescuePressureBypassStepMaxEntries = 3;
const int    InpTrendRescuePressureConfirmLookbackBars = 2;
const double InpTrendRescuePressureConfirmMoveUSD = 0.75;
const bool   InpUseAdaptiveTrendRescueCadence = true;
const int    InpTrendRescueMinCooldownSec = 12;
const double InpTrendRescueMinStepUSD = 0.70;
const double InpTrendRescueSkipDiagGapUSD = 80.0;
const bool   InpUseAdaptiveTrendRescueHarvest = true;
const double InpTrendRescueHarvestGapShare = 0.08;
const double InpTrendRescueMinAdaptiveHarvestUSD = 8.0;
const double InpTrendRescueMaxAdaptiveHarvestUSD = 120.0;

//--- Recovery cleanup - loser ---------------------------------------
const bool   InpUseTrendRescueLoserCleanup = true;
const double InpTrendRescueCleanupBufferUSD = 2.0;
const int    InpTrendRescueCleanupMaxActionsPerTick = 2;
const bool   InpTrendRescueUsePartialLoserCleanup = true;
const bool   InpUseTrendRescueEntryLoserCleanup = true;
const double InpTrendRescueEntryCleanupBufferUSD = 2.0;
const int    InpTrendRescueEntryCleanupMaxActionsPerTick = 2;
const bool   InpTrendRescueEntryUsePartialCleanup = true;
const int    InpTrendRescueEntryMinAgeSec = 300;

//--- Recovery cleanup - pairs ---------------------------------------
const bool   InpUseTrendRescuePairFundedCleanup = true;
const double InpPairCleanupMinProfitUSD = 5.0;
const double InpPairCleanupBufferUSD = 2.0;
const int    InpPairCleanupMaxActionsPerTick = 2;
const bool   InpPairCleanupPreferOldGridLosers = true;
const int    InpPairCleanupReserveExpirySec = 600;

//--- Recovery cleanup - floating ------------------------------------
const bool   InpUseTrendRescueFloatingPairCleanup = true;
const double InpFloatingPairCleanupProfitShare = 0.80;
const int    InpFloatingPairCleanupMaxProfitTicketsPerTick = 4;
const int    InpFloatingPairCleanupMaxLoserActionsPerTick = 2;
const double InpFloatingPairCleanupMinMarginLevelPct = 500.0;
const double InpFloatingPairCleanupMinEquityBufferUSD = 150.0;

//--- Recovery cleanup - stale / equity pressure ---------------------
const bool   InpUseEquityPressureMode = true;
const double InpEquityPressureDDUSD = 1500.0;
const double InpEquityPressureSevereDDUSD = 3000.0;
const double InpEquityPressureCooldownMultiplier = 2.0;
const double InpEquityPressureStepMultiplier = 1.5;
const double InpEquityPressureLotMultiplier = 0.50;
const bool   InpEquityPressureDisableContinuationOverride = true;
const int    InpEquityPressureMaxTrendEntries = 8;
const bool   InpUseStaleTradeCleanup = true;
const int    InpStaleTradeMinAgeMinutes = 240;
const int    InpStaleTradeTriggerCount = 20;
const double InpStaleTradeLargestLossTriggerUSD = 400.0;
const double InpStaleTradeTotalLossTriggerUSD = 1200.0;
const double InpStaleTradeMaxLossPerTickUSD = 25.0;
const double InpStaleTradeMaxLossPerHourUSD = 100.0;
const double InpStaleTradeMinEquityDDUSD = 1000.0;
const int    InpStaleTradeMaxActionsPerTick = 2;

//--- Recovery cleanup - protected -----------------------------------
const bool   InpUseProtectedProfitCleanup = true;
const double InpProtectedProfitFloorUSD = 25.0;
const double InpProtectedProfitCleanupBufferUSD = 2.0;
const int    InpProtectedProfitCleanupMaxActionsPerTick = 2;

//--- Recovery cleanup - stuck / direction / guards ------------------
const bool   InpUseStuckRecoveryCleanup = true;
const double InpStuckRecoveryGapUSD = 500.0;
const double InpStuckRecoveryBalanceCushionUSD = 100.0;
const double InpStuckRecoverySpendShare = 0.25;
const double InpStuckRecoveryMaxSpendUSD = 75.0;
const double InpStuckRecoveryMinEquityBufferUSD = 100.0;
const int    InpStuckRecoveryMaxActionsPerTick = 2;
const bool   InpUseRollingRecoveryDirection = true;
const bool   InpRecoveryDirectionResetEntryGate = true;
const int    InpRecoveryDirectionLookbackBars = 3;
const double InpRecoveryDirectionMinMoveUSD = 2.0;
const int    InpRecoveryDirectionSwitchCooldownSec = 120;
const bool   InpUseTrendRescueNoEffectGuard = true;
const double InpNoEffectMinCoverageImprovePct = 2.5;
const double InpNoEffectMinCleanupFundingUSD = 35.0;
const double InpNoEffectExpectedMoveATRShare = 0.50;
const bool   InpNoEffectAllowIfNoPositions = true;
const bool   InpUseTrendRescueOppositeExposureGuard = true;
const double InpOppositeExposureMaxLots = 0.08;
const bool   InpOppositeExposureRequireNetReduce = true;
const double InpOppositeExposureHardMaxLots = 0.0;
const int    InpTrendRescueSkipLogThrottleSec = 10;

//--- Basket averaging (master OFF) ----------------------------------
const bool   InpUseBasketAveraging      = false;
const double InpAvgTriggerLossUSD        = 0.0;
const double InpAvgStepUSD               = 0.0;
const double InpAvgLot                   = 0.0;
const int    InpAvgMaxEntries            = 0;
const double InpAvgMaxLots               = 0.0;
const double InpAvgMinMarginLevelPct     = 500.0;
const int    InpAvgCooldownSec           = 60;

//--- Trailing extras ------------------------------------------------
const int    InpTrendRescueTrailArmEntries = 1;
const int    InpTrailModifyMinSeconds = 10;
const double InpTrailModifyMinStepUSD = 0.50;

//--- Execution / safety ---------------------------------------------
const int    InpMaxRetries       = 3;
const int    InpRetryBackoffMs   = 300;
const int    InpDeviationPoints  = 50;
const int    InpRetrySeconds     = 30;
const int    InpManageThrottleSeconds = 0;
const int    InpMarkerStaleHours = 72;

//--- Market validation safety ---------------------------------------
const bool   InpUseMarketValidationSafety = true;
const double InpMarketValidationSmallAccountEquityUSD = 1000.0;
const double InpMarketValidationMaxLotInflationRatio = 3.0;
const double InpMarketValidationMaxInflatedLotSmallAccount = 0.10;
const double InpMarketValidationMinFreeMarginAfterCheckUSD = 200.0;
const double InpMarketValidationMinMarginLevelAfterCheckPct = 500.0;

//--- Chart UI extras (public toggle is InpShowStraddleChartUI only) -
const bool   InpUseStraddleChartSkin = true;
const bool   InpShowStraddleProfitMarkers = true;
const int    InpStraddleUIDashboardRefreshSeconds = 2;
const int    InpStraddleProfitMarkerLookbackDays = 14;
const int    InpStraddleMaxProfitMarkers = 40;

#define LOWPRICE_TARGET_STEP_POINTS 100   // 1.0.4: effective grid step (points) for low-priced symbols; 100pt = 10 pips on 5-digit FX
#define LOWPRICE_MAX_LEVELS         3     // 1.0.4: cap stop orders per side for low-priced symbols
#define ORDER_LIMIT_SAFETY_BUFFER   4     // 1.0.6: headroom kept below ACCOUNT_LIMIT_ORDERS when capping simultaneous pendings

//--- Canonical production trade boundary ---------------------------
// Immutable strategy intent is separated from the volatile account/symbol
// snapshot used to build and check the one request that is finally sent.
enum ENUM_STR_OPERATION_KIND
  {
   STR_OP_INITIALIZE=0,
   STR_OP_PENDING_PLACE,
   STR_OP_MARKET_DEAL,
   STR_OP_SLTP,
   STR_OP_PENDING_MODIFY,
   STR_OP_POSITION_REDUCE,
   STR_OP_PENDING_DELETE,
   STR_OP_FLOAT_REANCHOR
  };

enum ENUM_STR_ACTION_STATE
  {
   REJECTED=0,
   DEFERRED,
   COMPLETED,
   PARTIAL,
   PENDING_RECONCILIATION
  };

enum ENUM_STR_REASON_CODE
  {
   STR_REASON_NONE=0,
   DEFER_MARKET_CLOSED,
   DEFER_PRICE_CHANGED,
   DEFER_SERVER_BUSY,
   DEFER_PENDING_DELETE,
   DEFER_TRANSACTION_RECONCILIATION,
   DEFER_FLOAT_FINANCIAL_SNAPSHOT,
   DEFER_FLOAT_CHECKPOINT_PERSIST,
   REJECT_ACCOUNT_CAPABILITY,
   REJECT_SYMBOL_CAPABILITY,
   REJECT_EXPIRATION_UNSUPPORTED,
   REJECT_FILLING_UNSUPPORTED,
   REJECT_ORDERCHECK_FALSE,
   REJECT_ORDERCHECK_RETCODE,
   REJECT_MARGIN_PROJECTION,
   REJECT_STOPOUT_PROJECTION,
   REJECT_EXPOSURE_CAP,
   REJECT_INVALID_INPUT,
   REJECT_FLOAT_CHECKPOINT_INVALID,
   PARTIAL_VOLUME_CONFIRMED
  };

#define STR_MAX_PENDING_ACTIONS 64
#define STR_SEEN_TX_CAPACITY 256
#define STR_SEEN_DEAL_CAPACITY 256
#define STR_MAX_TX_WORK 64

enum ENUM_STR_PENDING_RESERVATION_RESULT
  {
   STR_PENDING_RESERVATION_OK=0,
   STR_PENDING_RESERVATION_ALREADY_TRACKED,
   STR_PENDING_RESERVATION_FULL
  };

struct StrTradeIntent
  {
   ENUM_STR_OPERATION_KIND kind;
   ENUM_ORDER_TYPE order_type;
   string symbol;
   long magic;
   double requested_volume;
   double fixed_price;
   double stoplimit;
   double sl;
   double tp;
   uint deviation;
   string comment;
   ulong order;
   ulong order_identifier;
   long order_setup_time_msc;
   ulong position;
   ulong position_identifier;
   ulong position_by;
   ENUM_ORDER_TYPE_TIME original_time_type;
   datetime original_expiration;
   double original_price;
   double original_stoplimit;
   double original_sl;
   double original_tp;
   ENUM_ORDER_TYPE_TIME requested_time_type;
   datetime requested_expiration;
   bool change_price;
   bool change_stoplimit;
   bool change_sl;
   bool change_tp;
   bool change_time_policy;
   bool new_exposure;
   double margin_floor_pct;
   double free_margin_reserve;
  };

struct StrTradeSnapshot
  {
   datetime captured_at;
   double bid;
   double ask;
   double last;
   double point;
   double volume_min;
   double volume_max;
   double volume_step;
   double volume_limit;
   double directional_volume;
   double equity;
   double current_margin;
   double current_free_margin;
   double stopout_call;
   double stopout_stop;
   int digits;
   int stops_level;
   int freeze_level;
   int pending_count;
   int position_count;
   long trade_mode;
   long execution_mode;
   long order_mode;
   long expiration_mode;
   long filling_mode;
   long stopout_mode;
   long margin_mode;
   long account_order_limit;
   bool account_trade_allowed;
   bool account_expert_allowed;
   bool terminal_trade_allowed;
   bool mql_trade_allowed;
   bool hedging;
   bool hedge_allowed;
   bool fifo_close;
   ulong generation;
  };

struct StrActionOutcome
  {
   ENUM_STR_OPERATION_KIND kind;
   ENUM_STR_REASON_CODE reason;
   ENUM_STR_ACTION_STATE state;
   uint request_id;
   double requested_volume;
   double confirmed_volume;
   double remaining_volume;
   double confirmed_net_money;
   uint retcode;
   uint retcode_external;
   ulong order;
   ulong deal;
   double result_price;
   string result_comment;
   bool terminal_result;
   int last_error;
  };

struct StrPendingAction
  {
   bool active;
   bool reserved_before_send;
   string identity;
   StrTradeIntent intent;
   StrActionOutcome outcome;
   double cumulative_confirmed;
   double confirmed_profit;
   double confirmed_commission;
   double confirmed_swap;
   double confirmed_fee;
   double provisional_result_volume;
   double original_requested_volume;
   double current_leg_requested_volume;
   ulong order;
   ulong deal;
   ulong initial_deal;
   ulong position_id;
   ulong generation;
   datetime updated_at;
   bool terminal_ready;
   bool terminal_consumed;
   bool terminal_short_fill;
   bool remainder_authorized;
   ENUM_ORDER_TYPE expected_type;
   double expected_price;
   double expected_volume;
   double expected_stoplimit;
   double expected_sl;
   double expected_tp;
   ENUM_ORDER_TYPE_TIME expected_time_type;
   datetime expected_expiration;
  };

struct StrSeenTransaction
  {
   bool used;
   string identity;
  };

struct StrSeenDeal
  {
   bool used;
   ulong deal_id;
  };

//--- Globals --------------------------------------------------------
CTrade   g_trade;
CSymbolInfo g_symbol;
string   g_sym;                  // symbol the EA runs on
int      g_digits        = 0;    // SYMBOL_DIGITS, read in OnInit
bool     g_lowPricedSymbol = false;   // 1.0.4: true for low-priced FX (e.g. EURUSD) -> symbol-native spacing/levels; false for XAUUSD (gold path unchanged)
string   g_accountCurrency = ""; // ACCOUNT_CURRENCY, read in OnInit
string   g_accountName = "";     // ACCOUNT_NAME, read in OnInit
string   g_accountServer = "";   // ACCOUNT_SERVER, read in OnInit
string   g_accountCompany = "";  // ACCOUNT_COMPANY, read in OnInit
string   g_moneyScaleReason = "normal"; // why g_moneyScale was selected
double   g_moneyScale = 1.0;     // user dollar-style input -> account-money multiplier
int      g_accountCurrencyDigits = 2; // ACCOUNT_CURRENCY_DIGITS, read in OnInit
datetime g_priceScaleCacheTime = 0; // per-second adaptive price-scale cache
double   g_priceScaleCacheFactor = 1.0;
double   g_priceScaleCacheMidPrice = 0.0;
double   g_priceScaleCacheATR = 0.0;
//--- 0.0.40 O3: persisted ATR indicator handle (created lazily, released in
//    OnDeinit). CopyBuffer is still called every ATRPriceDistance() call so the
//    returned forming-bar ATR is bit-identical; only handle create/destroy
//    churn is removed.
int      g_atrHandle = INVALID_HANDLE;
//--- 0.0.40 O1: per-tick whole-book aggregate cache. The EA is single-threaded
//    with NO OnTrade/OnTradeTransaction handler, so the position/order book
//    mutates ONLY at the 11 synchronous trade-op sites. g_bookDirty is set
//    true (a) at the top of ManageCycle (new tick -> price/time moved, so the
//    floating/PROFIT/SWAP and stale-age members are stale) and (b) at every
//    successful trade-op branch BEFORE any in-branch aggregate read. The first
//    cached read after any set re-walks the book ONCE (RecomputeBookAggregates)
//    and refreshes ALL members atomically, then clears the flag. Every cached
//    read therefore equals the value a fresh 0.0.39 scan would have produced at
//    that exact program point. CycleRealized (deal-history) and
//    CountLosingPositions are deliberately NOT part of this cache.
struct BookAggregates
  {
   // count-only members (pure function of book membership)
   int    myPositions;        // CountMyPositions
   int    myPendings;         // CountMyPendings (OrdersTotal pass)
   int    rescueHedges;       // CountRescueHedges
   int    trendEntries;       // CountTrendRescueEntries
   int    trendBuyEntries;    // CountTrendRescueEntriesForDirection(true)
   int    trendSellEntries;   // CountTrendRescueEntriesForDirection(false)
   int    avgEntries;         // CountAveragingEntries
   // volume-only members (price-independent)
   double buyLots;            // NetExposureLots buy
   double sellLots;           // NetExposureLots sell
   double openLots;           // OpenFloatingPL out-param
   double avgTotalLots;       // AveragingTotalLots
   double avgCoreBuyLots;     // AveragingCoreExposureLots buy
   double avgCoreSellLots;    // AveragingCoreExposureLots sell
   double trendBuySideLots;   // TrendRescueExposureLotsForSide(true)  (NormalizeDouble .,8)
   double trendSellSideLots;  // TrendRescueExposureLotsForSide(false) (NormalizeDouble .,8)
   double staleBuyLots;       // StaleOldGridExposureLotsForSide(true)  (NormalizeDouble .,8)
   double staleSellLots;      // StaleOldGridExposureLotsForSide(false) (NormalizeDouble .,8)
   // floating members (broker-live POSITION_PROFIT/POSITION_SWAP - price driven)
   double floating;           // OpenFloatingPL return (already minus commission*openLots)
   double avgCoreBuyFloating; // AveragingCoreSideFloating(true)
   double avgCoreSellFloating;// AveragingCoreSideFloating(false)
   // Float Re-anchor bucket. Whole-book lanes retain these positions for
   // account safety while cycle-scoped lanes subtract registered Float tickets.
   int    floatEntries;
   double floatLots;
   double floatFloating;
  };
BookAggregates g_bookCache;
bool     g_bookDirty = true;     // 0.0.40 O1: book/tick mutated -> recompute on next read
datetime g_lastManageProcess = 0;   // 0.0.45: throttle: timestamp of last ManageCycle pass that was NOT skipped
int      g_lastBookCountSeen  = -1; // 0.0.45: throttle: PositionsTotal()+OrdersTotal() at last non-skipped pass
datetime g_cycleStart    = 0;    // start of current cycle (realized-P/L history window)
bool     g_cycleStartTrusted = false; // exact runtime/persisted cycle-start marker is authoritative
datetime g_lastStaleRelease = 0;   // 0.0.43: timestamp of the last STALE-path release (runtime anti-churn cooldown timer; NOT persisted - resets to 0 on restart = at most one immediate stale release after restart, acceptable)
ulong    g_floatTickets[];          // 0.0.44: ticket registry for FLOAT RE-ANCHOR (empty unless InpUseFloatReanchor); per-tick membership truth for floated legs
int      g_floatCount    = 0;       // 0.0.44: count of valid entries in g_floatTickets[]
ulong    g_floatClosedPositionIds[];// 0.0.44: POSITION_IDs of floated legs that were green-closed (excluded from CycleRealized of the fresh cycle)
int      g_floatClosedCount = 0;    // 0.0.44: count of valid entries in g_floatClosedPositionIds[]
double   g_floatStaleMaxNetMoney = 0.0;
long     g_floatReanchorSeconds = 0;
long     g_floatStaleSeconds = 0;
long     g_floatStaleCooldownSeconds = 0;
double   g_normalizedFloatLotCap = 0.0;
double   g_normalizedAvgLotCap = 0.0;
double   g_normalizedTrendRescueEntryLotCap = 0.0;
double   g_normalizedOppositeExposureLotCap = 0.0;
double   g_normalizedOppositeExposureHardLotCap = 0.0;
double   g_normalizedMarketValidationInflatedLotCap = 0.0;
uint     g_testerWaitMs = 0;
long     g_profitMarkerLookbackSeconds = 0;
long     g_markerStaleSeconds = 0;
long     g_rescueHedgeMaxHoldSeconds = 0;
long     g_staleTradeMinAgeSeconds = 0;
int      g_validatedMaxRetries = 0;
uint     g_retryBackoffBaseMs = 0;
ulong    g_tradeSnapshotGeneration = 0;
StrPendingAction g_pendingActions[64];
StrSeenTransaction g_seenTransactions[256];
StrSeenDeal g_seenDeals[256];
int      g_seenTransactionCursor = 0;
int      g_seenDealCursor = 0;
bool     g_pendingRegistryFault = false;
datetime g_pauseUntil    = 0;    // no trading until this time (post-cycle pause)
bool     g_abortBuild    = false;// set when a hard limit retcode aborts a populate pass
bool     g_marginAbortBuild = false; // true when margin, not broker order count, capped a pass
int      g_marginAbortLevel = 0; // first level omitted by a margin cap in the current pass
datetime g_nextBuildTry  = 0;    // back-off: no build/populate attempt before this time
bool     g_buildSkipWait = false;// set when a market-closed/disabled retcode aborts a pass
bool     g_marketValidationMinLotInflationSkip = false; // true only for the min-lot-inflation zero-grid fallback path
bool     g_marketValidationOtherZeroPlacementCause = false; // true when any non-min-lot reason can explain zero grid orders
bool     g_wasTradeable  = true; // last observed tradeability (throttles the closed notice)
bool     g_tearingDown   = false;// persistent teardown: close decision taken, drive to flat
// 1.1.18/1.1.20 daily limits (EQUITY)
datetime g_dailyDayStamp = 0;          // server date 00:00 of current trading day
double   g_dayStartEquity = 0.0;       // 1.1.20: equity at day open (anchor)
double   g_dailyEquityPL = 0.0;        // 1.1.20: equity - dayStartEquity (includes floating)
bool     g_dailyTradingStopped = false;
string   g_dailyStopReason = "";
bool     g_dailyStopForceClose = false; // 1.1.20: force flat on daily limit (skip cycleNet hold)
// 1.1.19: after daily loss stop, next day uses 2x base lots
bool     g_pendingNextDayLot2x = false; // set when loss limit hits; consumed at next midnight
bool     g_recoveryLot2xToday  = false; // true for the recovery day only
// 1.1.35: multi-day no-trade after HARD daily LOSS limit (server-day stamps)
datetime g_lossCooldownResumeDay = 0; // first server day 00:00 when new trading is allowed (0=off)
bool     g_basketTearDown = false;// 0.0.38: current teardown was armed by the account-level basket take-profit; while set, ProcessTearDown re-checks total floating >= +threshold before closing so a resume/partial-close never banks a sub-threshold (red) book
datetime g_nextTearTry   = 0;    // back-off: no teardown attempt before this time
bool     g_rescueHolding = false;// persistent rescue: delete pendings, hold positions until flat/safe
datetime g_nextRescueTry = 0;    // back-off: no rescue pending-delete attempt before this time
double   g_rescueAnchorBalance = 0.0; // account balance anchor set when rescue is entered
bool     g_rescueAnchorTrusted = false; // false means rescue bank must be 0 with open exposure
datetime g_lastRescueStatusLog = 0; // throttle for periodic rescue status logs
datetime g_lastRescueHedgeTime = 0; // cooldown for rescue hedge opens
double   g_peakEquity        = 0.0;  // 0.0.37: persisted peak account equity (restart-robust; monitoring/logging only)
bool     g_equityBackstopArmed = false; // 0.0.37: equity-DD circuit breaker armed (hysteresis state)
bool     g_rescueHedgeModeLogged = false; // log non-hedging skip once
bool     g_rescueCloseByNoticeLogged = false; // close-by support intentionally not active
bool     g_trendRescueActive = false; // separate trend rescue: hold old basket, trade only active recovery direction
int      g_trendRescueDirection = 0;  // 1=BUY recovery after upside break, -1=SELL recovery after downside break
datetime g_lastTrendRescueDirectionSwitchTime = 0; // cooldown for rolling trend rescue direction switches
datetime g_lastTrendRescueEntryTime = 0; // cooldown for trend rescue entries
//--- 1.1.1: trend one-side grid cancel/hedge-block log throttle
int      g_trendOneSideBiasSeen = 0;           // last observed TrendOneSideBias() for state-change logging
datetime g_lastCounterTrendCancelLog = 0;      // last normal-level cancel summary log time
// 1.1.14: TREND-2X arm state for always-on logging + upgrade throttle
bool     g_trend2xWasActive = false;
bool     g_trend2xArmedThisCycle = false;      // latched once 2x arms; cleared on new cycle
int      g_trend2xLastDir   = 0;               // +1 up / -1 down while last ON
datetime g_trend2xLastLog   = 0;
datetime g_trend2xLastUpgrade = 0;             // min spacing between mass upgrades
datetime g_trend2xLastCleanupLog = 0;
datetime g_lastTrendOneSideHedgeBlockLog = 0;  // last hedge-block skip log time
int      g_lastTrendOneSideHedgeBlockBias = 0; // bias value of last hedge-block skip log
double   g_trendRescueLastEntryPrice = 0.0; // last accepted trend rescue market-entry price
datetime g_lastBasketAvgEntryTime = 0;      // 0.0.39: cooldown for bounded averaging-down adds
double   g_basketAvgLastEntryPrice = 0.0;   // 0.0.39: last accepted averaging add fill price (adverse-step reference)
double   g_protectedProfitAnchorBalance = 0.0; // runtime account-balance anchor for protected cleanup
datetime g_lastTrendRescueSkipLog = 0; // throttle noisy trend-rescue skip diagnostics
string   g_lastTrendRescueSkipReason = "";
double   g_lastTrendRescueSkipBucket = -1.0;
double   g_deferredPairProfitReserve = 0.0; // booked pair-profit budget reserved for next loser cleanup
datetime g_deferredPairProfitReserveTime = 0; // reserve creation/last-refresh time
datetime g_staleTradeLossHourStart = 0; // current stale-cleanup hourly budget bucket
double   g_staleTradeLossHourSpent = 0.0; // realized stale-cleanup loss spent in current hour
double   g_teardownSafeThreshold = 0.0; // close-all guard for the active teardown reason
double   g_anchor        = 0.0;  // FIXED cycle anchor (0.0.8): snapped mid at cycle start
double   g_cycleGridStepDistance = 0.0; // FIXED cycle grid step, snapshotted with anchor
string   g_lastOpMsg     = "";   // LogOp throttle: last operational message emitted
datetime g_lastOpTime    = 0;    // LogOp throttle: when it was emitted
datetime g_lastSlotSkipLog = 0;   // throttle strict fixed-slot skip diagnostics
datetime g_lastStatusHeartbeat = 0; // 1.1.3: ManageCycle STATUS heartbeat gate (120s)
datetime g_tradeJamUntil = 0;       // 1.1.7: quiet period after retcode-0 trade jam
long     g_lowLogSuppressedTrendSkips = 0; // tester low-log aggregate counters
long     g_lowLogSuppressedStaleStates = 0;
long     g_lowLogSuppressedNoEffectSkips = 0;
long     g_lowLogSuppressedOrderCheckZero = 0;
long     g_lowLogSuppressedModifySuccess = 0;
long     g_lowLogSuppressedModifyPrecall = 0;

#define STR_UI_PREFIX "STR_UI_"
#define STR_UI_DASH_BG STR_UI_PREFIX "DASH_BG"
#define STR_UI_SCAN STR_UI_PREFIX "SCAN"
#define STR_UI_SCAN_PREFIX STR_UI_PREFIX "SCAN_"
#define STR_UI_HALO_PREFIX STR_UI_PREFIX "HALO_"
#define STR_UI_SHIMMER_PREFIX STR_UI_PREFIX "SHIMMER_"
#define STR_UI_BACKGROUND STR_UI_PREFIX "BACKGROUND"
#define STR_UI_LINE_PREFIX STR_UI_PREFIX "LINE_"
#define STR_UI_MARK_PREFIX STR_UI_PREFIX "MARK_"
#define STR_UI_LOGO_RESOURCE "::StraddleLogo85.bmp"
#define STR_UI_BACKGROUND_RESOURCE_PREFIX "StraddleLogo85Dynamic_"

const bool   InpUseTesterLowLogMode = true;
const int    InpTesterLogSummarySec = 300;
const bool   InpSuppressSuccessfulTradeLogs = true;
const bool   InpSuppressRepeatedSkipLogs = true;
const bool   InpSuppressOrderCheckRetcodeZero = true;
const bool   InpLogStateChangeOnly = true;

datetime g_strUiLastDashboardRefresh = 0;
datetime g_strUiLastHistoryRefresh = 0;
datetime g_strUiLastLifetimeHistoryRefresh = 0;
datetime g_strUiLastMarkerRefresh = 0;
double   g_strUiTodayBooked = 0.0;
double   g_strUiWeekBooked = 0.0;
int      g_strUiActualOrdersPlaced = 0;
bool     g_strUiLifetimeHistoryReady = false;
bool     g_strUiChartSkinApplied = false;
bool     g_strUiBackgroundResourceChecked = false;
bool     g_strUiBackgroundDynamicReady = false;
bool     g_strUiBackgroundResourceWarningLogged = false;
uint     g_strUiBackgroundWidth = 512;
uint     g_strUiBackgroundHeight = 512;
string   g_strUiBackgroundResourceName = "";
string   g_strUiBackgroundResourcePath = "";
color    g_strUiOriginalColorBackground = clrNONE;
color    g_strUiOriginalColorForeground = clrNONE;
color    g_strUiOriginalColorGrid = clrNONE;
color    g_strUiOriginalColorChartUp = clrNONE;
color    g_strUiOriginalColorChartDown = clrNONE;
color    g_strUiOriginalColorCandleBull = clrNONE;
color    g_strUiOriginalColorCandleBear = clrNONE;
color    g_strUiOriginalColorBid = clrNONE;
color    g_strUiOriginalColorAsk = clrNONE;
color    g_strUiOriginalColorLast = clrNONE;
color    g_strUiOriginalColorStopLevel = clrNONE;

struct ThrottleKeyState
  {
   string   key;
   datetime last;
  };
ThrottleKeyState g_trendRescueCleanupDiagThrottle[];
ThrottleKeyState g_voluntaryLossFloorThrottle[];

struct LowLogKeyState
  {
   string   key;
   string   message;
   datetime last;
  };
LowLogKeyState g_lowLogStates[];

// 1.1.3: fixed-slot state-change log throttle (LogState)
#define LOG_STATE_MAX_KEYS 24
struct LogStateSlot
  {
   string   key;
   string   msg;
   datetime lastTime;
  };
LogStateSlot g_logStateSlots[LOG_STATE_MAX_KEYS];

struct TrendRescueCleanupNoActionGuard
  {
   string   lane;
   string   stateKey;
   datetime last;
  };
TrendRescueCleanupNoActionGuard g_trendRescueBelowMinLotGuards[];

struct TrendRescueSnapshot
  {
   bool   valid;
   bool   covered;
   double bookedProfit;
   double bookedLoss;
   double floatingLoss;
   double required;
   double cycleNet;
   double floating;
   double coverageGap;
   double effectiveHarvestTarget;
   int    positions;
   int    pendings;
   int    trendEntries;
   int    currentDirectionEntries;
   datetime stamp;
  };
TrendRescueSnapshot g_trendRescueSnapshot;

//+------------------------------------------------------------------+
//| Per-open-position peak tracking (0.0.9). The SL is broker-side;   |
//| after a restart the peaks are re-seeded at entry price            |
//| (documented approximation in RecoverCycleState).                  |
//+------------------------------------------------------------------+
struct LegTrack
  {
   ulong  ticket;   // open position ticket
   double peak;     // best favorable CLOSE price seen (buy: max bid, sell: min ask)
  };
LegTrack g_legs[];

struct TrailModifyThrottleState
  {
   ulong    ticket;
   datetime lastSuccess;
   double   lastSL;
  };
TrailModifyThrottleState g_trailModifyThrottle[];

//+------------------------------------------------------------------+
//| Candidate for profit-funded cleanup. Standard cleanup uses        |
//| profit+swap loss; trend-rescue cleanup includes close-cost budget |
//| in the candidate loss because it spends booked balance profit.    |
//+------------------------------------------------------------------+
struct CleanupCandidate
  {
   ulong  ticket;
   double volume;
   double loss;
   double lossPerLot;
   double swap;
   datetime openTime;
   ENUM_POSITION_TYPE type;
   bool   onLargestExposureSide;
  };

struct FloatingPairLoserCandidate
  {
   ulong  ticket;
   double volume;
   double loss;
   double lossPerLot;
   double swap;
   datetime openTime;
   ENUM_POSITION_TYPE type;
   bool   isTrendEntry;
  };

struct TrendRescueProfitCandidate
  {
   ulong  ticket;
   double volume;
   double profit;
   double estimatedCloseCost;
   datetime openTime;
   ENUM_POSITION_TYPE type;
  };

//+------------------------------------------------------------------+
//| Leveled logging (1.1.3):                                          |
//|   0 = errors / always-critical (LogAlways for true always-print)  |
//|   1 = state changes + cycle bank/tear/float success; refuse and   |
//|       status heartbeats are throttled via LogState                |
//|   2 = per-chunk detail and full init / money-input dumps          |
//+------------------------------------------------------------------+
string BoolText(const bool value)
  {
   return (value ? "true" : "false");
  }

bool IsTesterMode()
  {
   return (MQLInfoInteger(MQL_TESTER) != 0 ||
           MQLInfoInteger(MQL_OPTIMIZATION) != 0);
  }

bool TesterLowLogActive()
  {
   return (InpUseTesterLowLogMode && IsTesterMode());
  }

int EffectiveLogLevel()
  {
   return InpLogLevel;
  }

int TesterLowLogSummarySec()
  {
   if(InpTesterLogSummarySec < 1)
      return 1;
   return InpTesterLogSummarySec;
  }

int LowLogStateIndex(const string key)
  {
   int n = ArraySize(g_lowLogStates);
   for(int i = 0; i < n; i++)
     {
      if(g_lowLogStates[i].key == key)
         return i;
     }
   return -1;
  }

void RecordLowLogSuppressedEvent(const string key)
  {
   if(StringFind(key, "trend-skip|") == 0)
      g_lowLogSuppressedTrendSkips++;
   if(StringFind(key, "trend-skip|no_effect_guard") == 0)
      g_lowLogSuppressedNoEffectSkips++;
   if(StringFind(key, "stale-status|") == 0)
      g_lowLogSuppressedStaleStates++;
   if(StringFind(key, "ordercheck-retcode-zero|") == 0)
      g_lowLogSuppressedOrderCheckZero++;
   if(StringFind(key, "position-modified|") == 0)
      g_lowLogSuppressedModifySuccess++;
  }

void LogTesterLowLogSuppressedSummary(const string reason)
  {
   if(!TesterLowLogActive())
      return;

   long total = g_lowLogSuppressedTrendSkips + g_lowLogSuppressedStaleStates +
                g_lowLogSuppressedNoEffectSkips + g_lowLogSuppressedOrderCheckZero +
                g_lowLogSuppressedModifySuccess + g_lowLogSuppressedModifyPrecall;
   if(total <= 0)
      return;

   Print(StringFormat("Straddle: tester low-log suppressed summary reason=%s trendSkips=%I64d staleStates=%I64d noEffectSkips=%I64d orderCheckRetcodeZero=%I64d modifySuccessLogs=%I64d modifyPrecallSkips=%I64d",
                      reason,
                      g_lowLogSuppressedTrendSkips,
                      g_lowLogSuppressedStaleStates,
                      g_lowLogSuppressedNoEffectSkips,
                      g_lowLogSuppressedOrderCheckZero,
                      g_lowLogSuppressedModifySuccess,
                      g_lowLogSuppressedModifyPrecall));
  }

bool ShouldEmitLog(const int level,
                   const string key,
                   const string message = "",
                   const bool forceSummary = false)
  {
   if(level > EffectiveLogLevel())
      return false;
   if(!TesterLowLogActive() || key == "")
      return true;

   datetime now = TimeCurrent();
   int index = LowLogStateIndex(key);
   if(index < 0)
     {
      int n = ArraySize(g_lowLogStates);
      ArrayResize(g_lowLogStates, n + 1);
      g_lowLogStates[n].key = key;
      g_lowLogStates[n].message = message;
      g_lowLogStates[n].last = now;
      return true;
     }

   if(forceSummary || g_lowLogStates[index].message != message ||
      (now - g_lowLogStates[index].last) >= TesterLowLogSummarySec())
     {
      g_lowLogStates[index].message = message;
      g_lowLogStates[index].last = now;
      return true;
     }

   RecordLowLogSuppressedEvent(key);
   return false;
  }

void Log(const int level, const string msg)
  {
   if(level <= EffectiveLogLevel())
      Print(msg);
  }

//+------------------------------------------------------------------+
//| 0.0.40 O2: gate-before-format. Log() already prints only when      |
//| level<=EffectiveLogLevel(), but the StringFormat(...) argument is  |
//| ALWAYS built by the caller first. This macro textually inlines the |
//| same gate so the (often long, multi-%) StringFormat is only        |
//| evaluated when it would actually be printed. EffectiveLogLevel()   |
//| just returns InpLogLevel (no side effects), so the emitted text is |
//| byte-identical when the gate passes and absent when it fails -     |
//| exactly as Log() already decides, only the wasted format build is  |
//| removed. ONLY use at hot/per-tick sites whose StringFormat args are |
//| side-effect-free (no throttle/cache-mutating calls); LogOp /        |
//| ShouldEmitLog sites are NOT converted (LogOp is a dedup throttle    |
//| that must keep building its string).                                |
//+------------------------------------------------------------------+
#define LogFmt(lvl, fmtExpr) { if((lvl) <= EffectiveLogLevel()) Log((lvl), (fmtExpr)); }

void LogAlways(const string msg)
  {
   Print(msg);
  }

void LogTesterLowLogSettings()
  {
   Print(StringFormat("Straddle: tester low-log settings tester=%s optimization=%s active=%s effectiveLogLevel=%d summarySec=%d suppressSuccessfulTradeLogs=%s suppressRepeatedSkipLogs=%s suppressOrderCheckRetcodeZero=%s logStateChangeOnly=%s trailModifyMinSeconds=%d trailModifyMinStepUSD=%.2f",
                      BoolText(MQLInfoInteger(MQL_TESTER) != 0),
                      BoolText(MQLInfoInteger(MQL_OPTIMIZATION) != 0),
                      BoolText(TesterLowLogActive()),
                      EffectiveLogLevel(),
                      TesterLowLogSummarySec(),
                      BoolText(InpSuppressSuccessfulTradeLogs),
                      BoolText(InpSuppressRepeatedSkipLogs),
                      BoolText(InpSuppressOrderCheckRetcodeZero),
                      BoolText(InpLogStateChangeOnly),
                      InpTrailModifyMinSeconds,
                      InpTrailModifyMinStepUSD));
  }

//+------------------------------------------------------------------+
//| Throttled operational-log chokepoint (0.0.5 flood fix): a         |
//| REPEATED IDENTICAL operational failure line (close/delete/place   |
//| retcode errors that retry every tick / timer second) is emitted   |
//| at most once per InpRetrySeconds. A DIFFERENT message always      |
//| prints immediately, so distinct errors are never hidden.          |
//+------------------------------------------------------------------+
void LogOp(const string msg)
  {
   if(msg == g_lastOpMsg && (TimeCurrent() - g_lastOpTime) < InpRetrySeconds)
      return;
   g_lastOpMsg  = msg;
   g_lastOpTime = TimeCurrent();
   Print(msg);
  }

//+------------------------------------------------------------------+
//| 1.1.7: global trade-jam after empty retcode=0 floods (Jan19).     |
//+------------------------------------------------------------------+
void NoteTradeJam(const uint rc, const string where)
  {
   if(rc != 0)
      return;
   int cool = InpRetrySeconds;
   if(cool < 3)
      cool = 3;
   if(cool > 30)
      cool = 30;
   datetime until = TimeCurrent() + cool;
   if(until > g_tradeJamUntil)
      g_tradeJamUntil = until;
   LogState(1, "trade-jam",
            StringFormat("Straddle: ERR trade-jam retcode=0 at %s - backoff %ds", where, cool),
            30);
  }

bool TradeJamActive()
  {
   return (g_tradeJamUntil > 0 && TimeCurrent() < g_tradeJamUntil);
  }

//+------------------------------------------------------------------+
//| 1.1.3/1.1.6: per-KEY throttle (not key+msg).                      |
//| Same key prints at most once per throttleSec — prevents 2GB logs  |
//| when a value like floatPL changes every tick.                     |
//| Use a NEW key for a true new error class that must print ASAP.    |
//| level>0 respects EffectiveLogLevel. Returns true if printed.      |
//+------------------------------------------------------------------+
bool LogState(const int level, const string key, const string msg, const int throttleSec = 60)
  {
   if(level > 0 && level > EffectiveLogLevel())
      return false;

   datetime now = TimeCurrent();
   int found = -1;
   int freeIdx = -1;
   int oldestIdx = 0;
   datetime oldestTime = 0;
   for(int i = 0; i < LOG_STATE_MAX_KEYS; i++)
     {
      if(g_logStateSlots[i].key == key)
        {
         found = i;
         break;
        }
      if(freeIdx < 0 && g_logStateSlots[i].key == "")
         freeIdx = i;
      if(i == 0 || g_logStateSlots[i].lastTime < oldestTime)
        {
         oldestTime = g_logStateSlots[i].lastTime;
         oldestIdx = i;
        }
     }

   if(found >= 0)
     {
      // 1.1.6: throttle by KEY only (ignore msg churn)
      if(throttleSec > 0 &&
         g_logStateSlots[found].lastTime > 0 &&
         (now - g_logStateSlots[found].lastTime) < throttleSec)
         return false;
      g_logStateSlots[found].msg = msg;
      g_logStateSlots[found].lastTime = now;
      Print(msg);
      return true;
     }

   int slot = (freeIdx >= 0 ? freeIdx : oldestIdx);
   g_logStateSlots[slot].key = key;
   g_logStateSlots[slot].msg = msg;
   g_logStateSlots[slot].lastTime = now;
   Print(msg);
   return true;
  }

void InvalidateTrendRescueSnapshot()
  {
   ZeroMemory(g_trendRescueSnapshot);
   g_trendRescueSnapshot.valid = false;
  }

bool TrendRescueHotSkipReason(const string reason)
  {
   return (reason == "continuation_step" ||
           reason == "missing_direction" ||
           reason == "coverage_pressure_continuation_override" ||
           reason == "continuation_pressure_override" ||
           reason == "adaptive_target_reached" ||
           reason == "max_entries" ||
           reason == "pressure_target_total_safety_cap" ||
           reason == "pressure_target_slot_clip" ||
           reason == "pressure_target_lot_clip" ||
           reason == "cooldown" ||
           reason == "equity_pressure_backpressure" ||
           reason == "equity_pressure_continuation_override_disabled" ||
           reason == "stale_backpressure" ||
           reason == "no_effect_guard" ||
           reason == "opposite_str_exposure_guard" ||
           reason == "opposite_str_exposure_hardcap" ||
           reason == "margin_level" ||
           reason == "margin_level_after_check" ||
           reason == "free_margin" ||
           reason == "margin_free_after_check" ||
           reason == "adaptive_lot_margin" ||
           reason == "adaptive_lot_margin_retry" ||
            reason == "adaptive_lot_min_exceeds_cap" ||
            reason == "adaptive_lot_too_small" ||
            reason == "lot_too_small" ||
            reason == "profit_covered_but_cycleNet_guard" ||
            reason == "no_price");
  }

double TrendRescueSkipCoverageBucket(const double coverageGap)
  {
   double bucketSize = MoneyInput(InpTrendRescueSkipDiagGapUSD);
   if(bucketSize <= 0.0)
      bucketSize = 1.0;
   return MathFloor(MathMax(0.0, coverageGap) / bucketSize);
  }

int TrendRescueCleanupDiagThrottleSec()
  {
   if(TesterLowLogActive() && InpSuppressRepeatedSkipLogs)
      return TesterLowLogSummarySec();

   int throttle = InpTrendRescueSkipLogThrottleSec;
   if(InpRescueStatusLogSeconds > 0 && (throttle <= 0 || InpRescueStatusLogSeconds < throttle))
      throttle = InpRescueStatusLogSeconds;
   if(throttle <= 0)
      throttle = 15;
   return throttle;
  }

int TrendRescueCleanupDiagThrottleIndex(const string key)
  {
   int n = ArraySize(g_trendRescueCleanupDiagThrottle);
   for(int i = 0; i < n; i++)
     {
      if(g_trendRescueCleanupDiagThrottle[i].key == key)
         return i;
     }
   return -1;
  }

bool TrendRescueCleanupDiagLogAllowed(const string key, const bool force)
  {
   string lowLogKey = "stale-status|" + key;
   if(TesterLowLogActive() && InpSuppressRepeatedSkipLogs && InpLogStateChangeOnly &&
      !ShouldEmitLog(0, lowLogKey, key, force))
      return false;

   datetime now = TimeCurrent();
   int throttle = TrendRescueCleanupDiagThrottleSec();
   int index = TrendRescueCleanupDiagThrottleIndex(key);
   if(index >= 0)
     {
      if(!force &&
         g_trendRescueCleanupDiagThrottle[index].last > 0 &&
         (now - g_trendRescueCleanupDiagThrottle[index].last) < throttle)
         return false;
      g_trendRescueCleanupDiagThrottle[index].last = now;
      return true;
     }

   int n = ArraySize(g_trendRescueCleanupDiagThrottle);
   ArrayResize(g_trendRescueCleanupDiagThrottle, n + 1);
   g_trendRescueCleanupDiagThrottle[n].key = key;
   g_trendRescueCleanupDiagThrottle[n].last = now;
   return true;
  }

string TrendRescueCleanupDiagNumericToken(const string text, const string token)
  {
   string prefix = token + "=";
   int pos = StringFind(text, prefix);
   if(pos < 0)
      return "";

   int start = pos + StringLen(prefix);
   int len = StringLen(text);
   int end = start;
   while(end < len)
     {
      ushort ch = StringGetCharacter(text, end);
      if((ch >= 48 && ch <= 57) || ch == 45 || ch == 43 || ch == 46)
         end++;
      else
         break;
     }
   if(end <= start)
      return "";
   return StringSubstr(text, start, end - start);
  }

string TrendRescueCleanupDiagMoneyBucket(const string extra)
  {
   string raw = TrendRescueCleanupDiagNumericToken(extra, "budget");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "budgetBefore");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "currentBudget");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "remainingTickBudget");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "actualFloorSafeLoserBudget");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "floorBudget");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "floorBalance");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "floorEquity");
   if(raw == "")
      return "none";
   return MoneyThrottleBucketKey(StringToDouble(raw));
  }

string TrendRescueCleanupDiagCountBucket(const string extra)
  {
   string raw = TrendRescueCleanupDiagNumericToken(extra, "candidates");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "oldLosingCandidates");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "redEntryCandidates");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "profitCandidates");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "oldCount");
   if(raw == "")
      raw = TrendRescueCleanupDiagNumericToken(extra, "redCount");
   if(raw == "")
      return "none";
   long count = StringToInteger(raw);
   if(count < 0)
      count = 0;
   return IntegerToString(count);
  }

string TrendRescueCleanupDiagStableKey(const string mode,
                                       const string reason,
                                       const string extra)
  {
   string coverageBucket = DoubleToString(TrendRescueSkipCoverageBucket(TrendRescueCoverageGap()), 0);
   return StringFormat("%s|%s|coverage=%s|budget=%s|count=%s",
                       mode, reason, coverageBucket,
                       TrendRescueCleanupDiagMoneyBucket(extra),
                       TrendRescueCleanupDiagCountBucket(extra));
  }

void LogTrendRescueCleanupDiag(const string mode,
                               const string reason,
                               const string extra = "",
                               const bool force = false)
  {
   string key = TrendRescueCleanupDiagStableKey(mode, reason, extra);
   if(!TrendRescueCleanupDiagLogAllowed(key, force))
      return;

   string msg = StringFormat("Straddle: trend rescue cleanup diag: mode=%s reason=%s", mode, reason);
   if(extra != "")
      msg += " " + extra;
   // 1.1.3: gate level-1 diag through LogState so identical lines never tick-spam
   LogState(1, "tr-cleanup-diag", msg, 60);
  }

string LowerCopy(const string value)
  {
   string copy = value;
   StringToLower(copy);
   return copy;
  }

string UpperCopy(const string value)
  {
   string copy = value;
   StringToUpper(copy);
   return copy;
  }

bool ContainsNoCase(const string text, const string needle)
  {
   if(needle == "")
      return true;
   return (StringFind(LowerCopy(text), LowerCopy(needle)) >= 0);
  }

bool IsCentCurrency(const string currency)
  {
   string code = UpperCopy(currency);
   return (code == "USC" || code == "EUC" || code == "GBC" ||
           code == "CHC" || code == "AUC" || code == "CAC");
  }

bool DetectMoneyScale()
  {
   g_moneyScale = 1.0;
   g_moneyScaleReason = "normal";

   if(!MathIsValidNumber(InpMoneyScaleOverride) || InpMoneyScaleOverride<0.0)
      return RejectInvalidInput("InpMoneyScaleOverride","must be finite and nonnegative before money-scale detection");

   if(InpMoneyScaleOverride > 0.0)
     {
      g_moneyScale = InpMoneyScaleOverride;
      g_moneyScaleReason = "override";
      return true;
     }

   if(IsCentCurrency(g_accountCurrency))
     {
      g_moneyScale = 100.0;
      g_moneyScaleReason = "currency";
      return true;
     }

   string context = g_accountCurrency + " " + g_accountName + " " + g_accountServer + " " + g_accountCompany;
   if(ContainsNoCase(context, "usc") ||
      ContainsNoCase(context, "cent") ||
      ContainsNoCase(context, "micro"))
     {
      g_moneyScale = 100.0;
      g_moneyScaleReason = "heuristic";
      return true;
     }

   return true;
  }

double NormalizeAccountMoney(const double value)
  {
   return NormalizeDouble(value, g_accountCurrencyDigits);
  }

double MoneyInput(const double rawInput)
  {
   return NormalizeAccountMoney(rawInput * g_moneyScale);
  }

double AccountMoneyToDisplay(const double accountMoney)
  {
   if(g_moneyScale <= 0.0)
      return accountMoney;
   return accountMoney / g_moneyScale;
  }

bool IsAutoLotMode()
  {
   return (InpLotMode == LOT_MODE_AUTO_BALANCE);
  }

// Balance is deliberately used here, not equity: floating loss must not
// silently shrink the next order size while the daily equity hard stop is
// already protecting the open book.
double AutoLotBalanceDisplay()
  {
   double balance = AccountMoneyToDisplay(AccountInfoDouble(ACCOUNT_BALANCE));
   if(!MathIsValidNumber(balance) || balance <= 0.0)
      balance = InpAutoLotBaseBalanceUSD;
   return balance;
  }

double AutoLotScaleFactor()
  {
   if(!IsAutoLotMode())
      return 1.0;
   if(!MathIsValidNumber(InpAutoLotBaseBalanceUSD) || InpAutoLotBaseBalanceUSD <= 0.0)
      return 1.0;
   double scale = AutoLotBalanceDisplay() / InpAutoLotBaseBalanceUSD;
   if(!MathIsValidNumber(scale) || scale <= 0.0)
      scale = 1.0;
   // Keep an invalid/misconfigured account from producing an unbounded lot
   // request. Broker volume min/max still apply in NormalizeLot().
   return MathMax(0.01, MathMin(100.0, scale));
  }

double AutoPercentMoney(const double percent, const double balanceDisplay)
  {
   if(!MathIsValidNumber(percent) || percent <= 0.0 ||
      !MathIsValidNumber(balanceDisplay) || balanceDisplay <= 0.0)
      return 0.0;
   return MoneyInput(balanceDisplay * percent / 100.0);
  }

double AutoDailyBasisBalanceDisplay()
  {
   double basis = AccountMoneyToDisplay(g_dayStartEquity);
   if(!MathIsValidNumber(basis) || basis <= 0.0)
      basis = AutoLotBalanceDisplay();
   return basis;
  }

uint RetryBackoffDelayMs(const int attempt)
  {
   if(attempt<0 || attempt>g_validatedMaxRetries)
      return 0;
   const ulong multiplier=(ulong)attempt+1ULL;
   const ulong widened=(ulong)g_retryBackoffBaseMs*multiplier;
   if(widened>(ulong)UINT_MAX)
      return UINT_MAX;
   return (uint)widened;
  }

double CommissionPerLotEffective()
  {
   return MathMax(0.0, MoneyInput(InpCommissionPerLot));
  }

double EstimateSpreadCost(const double lots)
  {
   if(!InpUseAutoSpreadCost || lots <= 0.0)
      return 0.0;

   MqlTick tick;
   if(!SymbolInfoTick(g_sym, tick) || tick.bid <= 0.0 || tick.ask <= 0.0 || tick.ask < tick.bid)
      return 0.0;

   double buySpread = 0.0;
   double sellSpread = 0.0;
   bool buyOk = OrderCalcProfit(ORDER_TYPE_BUY, g_sym, lots, tick.ask, tick.bid, buySpread);
   bool sellOk = OrderCalcProfit(ORDER_TYPE_SELL, g_sym, lots, tick.bid, tick.ask, sellSpread);
   if(buyOk || sellOk)
      return MathMax(MathAbs(buySpread), MathAbs(sellSpread));

   double tickSize = SymbolInfoDouble(g_sym, SYMBOL_TRADE_TICK_SIZE);
   double tickValue = SymbolInfoDouble(g_sym, SYMBOL_TRADE_TICK_VALUE);
   if(tickSize <= 0.0 || tickValue <= 0.0)
      return 0.0;

   double spreadTicks = (tick.ask - tick.bid) / tickSize;
   if(spreadTicks <= 0.0)
      return 0.0;
   return MathMax(0.0, spreadTicks * tickValue * lots);
  }

double EstimatedOpenPositionCommission(const ulong ticket, const double closeLots)
  {
   if(!InpUseAutoCommissionEstimate || ticket == 0 || closeLots <= 0.0)
      return 0.0;
   if(!PositionSelectByTicket(ticket))
      return 0.0;

   long positionId = (long)PositionGetInteger(POSITION_IDENTIFIER);
   if(positionId <= 0 || !HistorySelectByPosition(positionId))
      return 0.0;

   double entryCost = 0.0;
   double entryLots = 0.0;
   int deals = HistoryDealsTotal();
   for(int i = 0; i < deals; i++)
     {
      ulong deal = HistoryDealGetTicket(i);
      if(deal == 0)
         continue;
      if(HistoryDealGetString(deal, DEAL_SYMBOL) != g_sym)
         continue;
      if((long)HistoryDealGetInteger(deal, DEAL_MAGIC) != InpMagic)
         continue;
      ENUM_DEAL_ENTRY entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal, DEAL_ENTRY);
      if(entry != DEAL_ENTRY_IN && entry != DEAL_ENTRY_INOUT)
         continue;

      double lots = HistoryDealGetDouble(deal, DEAL_VOLUME);
      if(lots <= 0.0)
         continue;
      double commission = HistoryDealGetDouble(deal, DEAL_COMMISSION);
      double fee = HistoryDealGetDouble(deal, DEAL_FEE);
      entryCost += MathAbs(commission) + MathAbs(fee);
      entryLots += lots;
     }

   if(entryLots <= 0.0 || entryCost <= 0.0)
      return 0.0;

   double estimatedRoundTripCostPerLot = (entryCost / entryLots) * 2.0;
   return MathMax(0.0, estimatedRoundTripCostPerLot * closeLots);
  }

double EstimatedCloseCost(const double closeLots, const ulong ticket = 0)
  {
   if(closeLots <= 0.0)
      return 0.0;

   double configuredCommission = CommissionPerLotEffective() * closeLots;
   double historyCommission = EstimatedOpenPositionCommission(ticket, closeLots);
   double commission = MathMax(configuredCommission, historyCommission);
   double spread = EstimateSpreadCost(closeLots);
   return MathMax(0.0, commission + spread);
  }

string MoneyThrottleBucketKey(const double value)
  {
   double bucketSize = MoneyInput(1.0);
   if(bucketSize <= 0.0)
      bucketSize = 1.0;
   double bucket = MathFloor(MathMax(0.0, value) / bucketSize);
   return DoubleToString(bucket, 0);
  }

string TrendRescueMoneyStateKey(const double value)
  {
   return DoubleToString(NormalizeAccountMoney(value), g_accountCurrencyDigits);
  }

string TrendRescueVolumeStateKey(const double value)
  {
   return DoubleToString(NormalizeDouble(value, 8), 8);
  }

int VoluntaryLossFloorThrottleIndex(const string key)
  {
   int n = ArraySize(g_voluntaryLossFloorThrottle);
   for(int i = 0; i < n; i++)
     {
      if(g_voluntaryLossFloorThrottle[i].key == key)
         return i;
     }
   return -1;
  }

bool VoluntaryLossFloorLogAllowed(const string context,
                                  const ulong ticket,
                                  const double budget,
                                  const double floor)
  {
   string key = StringFormat("%s|%I64u|budget=%s|floor=%s",
                             context, ticket,
                             MoneyThrottleBucketKey(budget),
                             MoneyThrottleBucketKey(floor));
   datetime now = TimeCurrent();
   int throttle = TrendRescueCleanupDiagThrottleSec();
   int index = VoluntaryLossFloorThrottleIndex(key);
   if(index >= 0)
     {
      if(g_voluntaryLossFloorThrottle[index].last > 0 &&
         (now - g_voluntaryLossFloorThrottle[index].last) < throttle)
         return false;
      g_voluntaryLossFloorThrottle[index].last = now;
      return true;
     }

   int n = ArraySize(g_voluntaryLossFloorThrottle);
   ArrayResize(g_voluntaryLossFloorThrottle, n + 1);
   g_voluntaryLossFloorThrottle[n].key = key;
   g_voluntaryLossFloorThrottle[n].last = now;
   return true;
  }

string TrendRescueCleanupCandidateState(CleanupCandidate &candidates[],
                                        const double budget,
                                        const double floorBuffer = -1.0)
  {
   int n = ArraySize(candidates);
   double floorBudget = -1.0;
   double actionBudget = budget;
   string floorBudgetKey = "none";
   if(floorBuffer >= 0.0)
     {
      floorBudget = VoluntaryLossBudget(floorBuffer);
      actionBudget = MathMin(budget, floorBudget);
      floorBudgetKey = TrendRescueMoneyStateKey(floorBudget);
     }

   double vmin = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   string state = StringFormat("candidates=%d:actionBudget=%s:floorBudget=%s",
                               n, TrendRescueMoneyStateKey(actionBudget),
                               floorBudgetKey);
   for(int i = 0; i < n; i++)
     {
      ulong ticket = candidates[i].ticket;
      double volume = candidates[i].volume;
      double liveLossPerLot = candidates[i].lossPerLot;
      double liveProfitAndSwap = -candidates[i].loss;
      bool selected = false;
      if(ticket > 0 && PositionSelectByTicket(ticket))
        {
         selected = true;
         double selectedVolume = PositionGetDouble(POSITION_VOLUME);
         double selectedProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         if(selectedVolume > 0.0)
           {
            volume = selectedVolume;
            liveProfitAndSwap = selectedProfitAndSwap;
            if(selectedProfitAndSwap < 0.0)
               liveLossPerLot = MathAbs(selectedProfitAndSwap) / selectedVolume;
           }
        }

      double closeCostOneLot = EstimatedCloseCost(1.0, ticket);
      double closeCostMinLot = (vmin > 0.0 ? EstimatedCloseCost(vmin, ticket) : 0.0);
      double budgetLossPerLot = liveLossPerLot + closeCostOneLot;
      double chunkLots = CleanupChunkLots(volume, budgetLossPerLot, actionBudget);
      double chunkLoss = 0.0;
      if(chunkLots > 0.0)
         chunkLoss = TrendRescueCleanupEstimatedLoss(ticket, liveProfitAndSwap, volume, chunkLots);

      state += StringFormat("|i=%d:t=%I64u:v=%s:loss=%s:lpl=%s:swap=%s:time=%I64d:type=%d:largest=%d:selected=%d:closeCost1=%s:closeCostMin=%s:budgetLpl=%s:chunk=%s:chunkLoss=%s:actionable=%d",
                             i,
                             ticket,
                             TrendRescueVolumeStateKey(volume),
                             TrendRescueMoneyStateKey(candidates[i].loss),
                             TrendRescueMoneyStateKey(candidates[i].lossPerLot),
                             TrendRescueMoneyStateKey(candidates[i].swap),
                             (long)candidates[i].openTime,
                             (int)candidates[i].type,
                             (candidates[i].onLargestExposureSide ? 1 : 0),
                             (selected ? 1 : 0),
                             TrendRescueMoneyStateKey(closeCostOneLot),
                             TrendRescueMoneyStateKey(closeCostMinLot),
                             TrendRescueMoneyStateKey(budgetLossPerLot),
                             TrendRescueVolumeStateKey(chunkLots),
                             TrendRescueMoneyStateKey(chunkLoss),
                             (chunkLots > 0.0 ? 1 : 0));
     }
   return state;
  }

string TrendRescueProfitCandidateState(TrendRescueProfitCandidate &candidates[])
  {
   int n = ArraySize(candidates);
   string state = StringFormat("profits=%d", n);
   for(int i = 0; i < n; i++)
     {
      state += StringFormat("|i=%d:t=%I64u:v=%s:profit=%s:closeCost=%s:time=%I64d:type=%d",
                            i,
                            candidates[i].ticket,
                            TrendRescueVolumeStateKey(candidates[i].volume),
                            TrendRescueMoneyStateKey(candidates[i].profit),
                            TrendRescueMoneyStateKey(candidates[i].estimatedCloseCost),
                            (long)candidates[i].openTime,
                            (int)candidates[i].type);
     }
   return state;
  }

string TrendRescueBelowMinLotStateKey(const string lane,
                                      CleanupCandidate &candidates[],
                                      const double budget,
                                      const double floorBuffer = -1.0,
                                      const string extra = "")
  {
   double vmin = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   double vstep = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP);
   double vmax = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MAX);
   return lane + "|budget=" + TrendRescueMoneyStateKey(budget) +
          "|minLot=" + TrendRescueVolumeStateKey(vmin) +
          "|lotStep=" + TrendRescueVolumeStateKey(vstep) +
          "|maxLot=" + TrendRescueVolumeStateKey(vmax) + "|" +
          TrendRescueCleanupCandidateState(candidates, budget, floorBuffer) + extra;
  }

int TrendRescueBelowMinLotGuardIndex(const string lane)
  {
   int n = ArraySize(g_trendRescueBelowMinLotGuards);
   for(int i = 0; i < n; i++)
     {
      if(g_trendRescueBelowMinLotGuards[i].lane == lane)
         return i;
     }
   return -1;
  }

bool TrendRescueBelowMinLotGuardActive(const string lane,
                                       const string stateKey)
  {
   int index = TrendRescueBelowMinLotGuardIndex(lane);
   if(index < 0)
      return false;
   if(g_trendRescueBelowMinLotGuards[index].stateKey != stateKey)
      return false;
   if(g_trendRescueBelowMinLotGuards[index].last <= 0)
      return false;
   return ((TimeCurrent() - g_trendRescueBelowMinLotGuards[index].last) <
           TrendRescueCleanupDiagThrottleSec());
  }

void TrendRescueBelowMinLotGuardRecord(const string lane,
                                       const string stateKey)
  {
   int index = TrendRescueBelowMinLotGuardIndex(lane);
   if(index < 0)
     {
      int n = ArraySize(g_trendRescueBelowMinLotGuards);
      ArrayResize(g_trendRescueBelowMinLotGuards, n + 1);
      index = n;
     }
   g_trendRescueBelowMinLotGuards[index].lane = lane;
   g_trendRescueBelowMinLotGuards[index].stateKey = stateKey;
   g_trendRescueBelowMinLotGuards[index].last = TimeCurrent();
  }

void TrendRescueBelowMinLotGuardClear(const string lane)
  {
   int index = TrendRescueBelowMinLotGuardIndex(lane);
   if(index < 0)
      return;
   g_trendRescueBelowMinLotGuards[index].stateKey = "";
   g_trendRescueBelowMinLotGuards[index].last = 0;
  }

void InvalidateTrendRescueBelowMinLotGuards()
  {
   ArrayResize(g_trendRescueBelowMinLotGuards, 0);
  }

string TrendRescuePairBelowMinLotStateKey(TrendRescueProfitCandidate &profits[],
                                           const double budgetBefore,
                                           const double balanceBefore)
  {
   CleanupCandidate oldLosers[];
   int oldCount = CollectTrendRescueCleanupCandidates(oldLosers);
   SortTrendRescueCleanupCandidates(oldLosers);

   CleanupCandidate redTrendEntries[];
   int redCount = CollectTrendRescueEntryCleanupCandidates(redTrendEntries, false, IsTrendRescueBuy());
   SortTrendRescueCleanupCandidates(redTrendEntries);

   string state = "pair|budget=" + TrendRescueMoneyStateKey(budgetBefore) +
                  "|balanceBefore=" + TrendRescueMoneyStateKey(balanceBefore) +
                  "|minLot=" + TrendRescueVolumeStateKey(SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN)) +
                  "|lotStep=" + TrendRescueVolumeStateKey(SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP)) +
                  "|maxLot=" + TrendRescueVolumeStateKey(SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MAX)) + "|" +
                  TrendRescueProfitCandidateState(profits) +
                  StringFormat("|oldCount=%d|redCount=%d|preferOldGrid=%d",
                               oldCount, redCount,
                               (InpPairCleanupPreferOldGridLosers ? 1 : 0));

   int profitCount = ArraySize(profits);
   for(int i = 0; i < profitCount; i++)
     {
      double balanceAfterCandidateProfit = balanceBefore + profits[i].profit - profits[i].estimatedCloseCost;
      double reservedPairLossBudget = VoluntaryLossBudgetAtBalance(balanceAfterCandidateProfit,
                                                                   MoneyInput(InpPairCleanupBufferUSD));
      double projectedLossBudget = MathMin(MathMax(0.0, budgetBefore + profits[i].profit - profits[i].estimatedCloseCost),
                                           reservedPairLossBudget);
      state += StringFormat("|p=%d:t=%I64u:projectedBudget=%s:reservedBudget=%s:balanceAfter=%s:old=",
                            i, profits[i].ticket,
                            TrendRescueMoneyStateKey(projectedLossBudget),
                            TrendRescueMoneyStateKey(reservedPairLossBudget),
                            TrendRescueMoneyStateKey(balanceAfterCandidateProfit));
      state += TrendRescueCleanupCandidateState(oldLosers, projectedLossBudget) + ":red=" +
               TrendRescueCleanupCandidateState(redTrendEntries, projectedLossBudget);
     }

   return state;
  }

double VoluntaryLossFloorBalance(const double extraBuffer)
  {
   double anchor = 0.0;
   if(g_protectedProfitAnchorBalance > 0.0)
      anchor = MathMax(anchor, g_protectedProfitAnchorBalance);
   if(g_rescueAnchorTrusted && g_rescueAnchorBalance > 0.0)
      anchor = MathMax(anchor, g_rescueAnchorBalance);
   if(anchor <= 0.0)
      anchor = AccountInfoDouble(ACCOUNT_BALANCE);

   double buffer = MathMax(0.0, extraBuffer);
   return anchor + MoneyInput(InpProtectedProfitFloorUSD) + buffer;
  }

double VoluntaryLossBudgetAtBalance(const double balance, const double extraBuffer)
  {
   double floor = VoluntaryLossFloorBalance(extraBuffer);
   return MathMax(0.0, balance - floor);
  }

double VoluntaryLossBudget(const double extraBuffer)
  {
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double floor = VoluntaryLossFloorBalance(extraBuffer);
   return MathMax(0.0, balance - floor);
  }

bool VoluntaryLossCloseAllowedAtBalance(const string context,
                                        const ulong ticket,
                                        const double balance,
                                        const double estimatedLoss,
                                        const double extraBuffer,
                                        double &budget)
  {
   budget = VoluntaryLossBudgetAtBalance(balance, extraBuffer);
   double floor = VoluntaryLossFloorBalance(extraBuffer);
   double postCloseBalance = balance - estimatedLoss;
   if(estimatedLoss <= budget + 1.0e-6 && postCloseBalance + 1.0e-6 >= floor)
      return true;

   if(VoluntaryLossFloorLogAllowed(context, ticket, budget, floor))
      Log(1, StringFormat("Straddle: voluntary loss close skipped: reason=voluntary_loss_floor context=%s ticket #%I64u balance=%.2f estimatedLoss=%.2f floor=%.2f budget=%.2f postCloseBalance=%.2f protectedAnchor=%.2f rescueAnchor=%.2f buffer=%.2f",
                          context, ticket, balance, estimatedLoss, floor, budget,
                          postCloseBalance, g_protectedProfitAnchorBalance,
                          (g_rescueAnchorTrusted ? g_rescueAnchorBalance : 0.0),
                          MathMax(0.0, extraBuffer)));
   return false;
  }

bool VoluntaryLossCloseAllowed(const string context,
                               const ulong ticket,
                               const double estimatedLoss,
                               const double extraBuffer,
                               double &budget)
  {
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   return VoluntaryLossCloseAllowedAtBalance(context, ticket, balance, estimatedLoss, extraBuffer, budget);
  }

double EquityFloatingDrawdown()
  {
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   return NormalizeAccountMoney(MathMax(0.0, balance - equity));
  }

bool EquityPressureActive(const double equityDD)
  {
   if(!InpUseEquityPressureMode)
      return false;
   double threshold = MoneyInput(InpEquityPressureDDUSD);
   if(threshold <= 0.0)
      return false;
   return (equityDD >= threshold);
  }

bool EquityPressureSevere(const double equityDD)
  {
   if(!InpUseEquityPressureMode)
      return false;
   double threshold = MoneyInput(InpEquityPressureSevereDDUSD);
   if(threshold <= 0.0)
      return false;
   return (equityDD >= threshold);
  }

double EquityPressureLotMultiplier(const double equityDD)
  {
   if(!EquityPressureActive(equityDD))
      return 1.0;
   return MathMax(0.0, MathMin(1.0, InpEquityPressureLotMultiplier));
  }

double EquityPressureCooldownMultiplier(const double equityDD)
  {
   if(!EquityPressureActive(equityDD))
      return 1.0;
   return MathMax(1.0, InpEquityPressureCooldownMultiplier);
  }

double EquityPressureStepMultiplier(const double equityDD)
  {
   if(!EquityPressureActive(equityDD))
      return 1.0;
   return MathMax(1.0, InpEquityPressureStepMultiplier);
  }

int EquityPressureAdjustedEntryCap(const int baseCap,
                                   const double equityDD,
                                   bool &capped)
  {
   capped = false;
   if(baseCap <= 0)
      return baseCap;
   if(!EquityPressureSevere(equityDD))
      return baseCap;
   if(InpEquityPressureMaxTrendEntries < 0)
      return baseCap;
   if(InpEquityPressureMaxTrendEntries >= baseCap)
      return baseCap;
   capped = true;
   return InpEquityPressureMaxTrendEntries;
  }

void LogEquityPressureStatus(const double coverageGap,
                             const int currentDirectionEntries,
                             const int currentDirectionEntryCap)
  {
   double equityDD = EquityFloatingDrawdown();
   bool active = EquityPressureActive(equityDD);
   bool severe = EquityPressureSevere(equityDD);
   if(!active)
      return;

   LogTrendRescueCleanupDiag("equity-pressure", (severe ? "severe" : "active"),
                             StringFormat("equityDD=%.2f activeThreshold=%.2f severeThreshold=%.2f lotMultiplier=%.2f cooldownMultiplier=%.2f stepMultiplier=%.2f currentDirectionEntries=%d currentDirectionCap=%d coverageGap=%.2f orphanedPairProfitReserve=%.2f",
                                          equityDD,
                                          MoneyInput(InpEquityPressureDDUSD),
                                          MoneyInput(InpEquityPressureSevereDDUSD),
                                          EquityPressureLotMultiplier(equityDD),
                                          EquityPressureCooldownMultiplier(equityDD),
                                          EquityPressureStepMultiplier(equityDD),
                                          currentDirectionEntries,
                                          currentDirectionEntryCap,
                                          coverageGap,
                                          g_deferredPairProfitReserve));
  }

void ResetStaleTradeHourBudgetIfNeeded()
  {
   datetime now = TimeCurrent();
   if(now <= 0)
      return;
   datetime hourStart = (datetime)((long)now - ((long)now % 3600));
   if(g_staleTradeLossHourStart != hourStart)
     {
      g_staleTradeLossHourStart = hourStart;
      g_staleTradeLossHourSpent = 0.0;
     }
  }

double StaleTradeHourlyLossRemaining()
  {
   ResetStaleTradeHourBudgetIfNeeded();
   double hourlyCap = MoneyInput(InpStaleTradeMaxLossPerHourUSD);
   if(hourlyCap <= 0.0)
      return 0.0;
   return MathMax(0.0, NormalizeAccountMoney(hourlyCap - g_staleTradeLossHourSpent));
  }

void RecordStaleTradeLossSpend(const double estimatedLoss)
  {
   if(estimatedLoss <= 0.0)
      return;
   ResetStaleTradeHourBudgetIfNeeded();
   g_staleTradeLossHourSpent = NormalizeAccountMoney(g_staleTradeLossHourSpent + estimatedLoss);
  }

int OrphanedPairProfitReserveExpirySec()
  {
   if(InpPairCleanupReserveExpirySec <= 0)
      return 600;
   return InpPairCleanupReserveExpirySec;
  }

void ResetOrphanedPairProfitReserve()
  {
   g_deferredPairProfitReserve = 0.0;
   g_deferredPairProfitReserveTime = 0;
  }

void ClearDeferredPairProfitReserve(const string reason)
  {
   if(g_deferredPairProfitReserve <= 0.0 && g_deferredPairProfitReserveTime <= 0)
      return;

   LogOp(StringFormat("Straddle: trend rescue pair cleanup reserve cleared: reason=%s orphanedPairProfitReserve=%.2f reserveTime=%I64d",
                      reason, g_deferredPairProfitReserve,
                      (long)g_deferredPairProfitReserveTime));
   ResetOrphanedPairProfitReserve();
  }

bool ExpireTrendRescuePairOrphanedReserveIfNeeded(const string reason)
  {
   if(g_deferredPairProfitReserve <= 0.0)
     {
      if(g_deferredPairProfitReserveTime > 0)
         g_deferredPairProfitReserveTime = 0;
      return false;
     }

   if(g_deferredPairProfitReserveTime <= 0)
     {
      ClearDeferredPairProfitReserve("missing_reserve_time");
      return true;
     }

   datetime now = TimeCurrent();
   if(now <= 0)
      return false;

   long ageSec = (long)(now - g_deferredPairProfitReserveTime);
   int expirySec = OrphanedPairProfitReserveExpirySec();
   if(ageSec >= expirySec)
     {
      LogOp(StringFormat("Straddle: trend rescue pair cleanup reserve expired: reason=%s orphanedPairProfitReserve=%.2f reserveTime=%I64d ageSec=%I64d expirySec=%d",
                         reason, g_deferredPairProfitReserve,
                         (long)g_deferredPairProfitReserveTime,
                         ageSec, expirySec));
      ResetOrphanedPairProfitReserve();
      return true;
     }

   return false;
  }

bool OrphanedPairProfitReservePending(const string reason)
  {
   if(ExpireTrendRescuePairOrphanedReserveIfNeeded(reason))
      return false;
   return (g_deferredPairProfitReserve > 0.0);
  }

void LogEffectiveMoneySettings()
  {
   // 1.1.3: one compact level-1 line for diagnosis; verbose dumps only at level 2
    Log(1, StringFormat("Straddle: cfg magic=%I64d levels=%d step=%.2f lotMode=%s lots=%.2f/%.2f/%.2f autoBase=%.2f autoLots=%.2f/%.2f/%.2f autoScale=%.4f target=%.2f basketTP=%.2f trendOneSide=%s float=%s trendRescue=%s trail=%s logLevel=%d moneyScale=%.2f",
                        InpMagic,
                        InpGridLevels,
                        InpGridStepUSD,
                        (IsAutoLotMode() ? "auto-balance" : "fixed"),
                        InpLotNear, InpLotMid, InpLotFar,
                        InpAutoLotBaseBalanceUSD,
                        InpAutoLotNear * AutoLotScaleFactor(),
                        InpAutoLotMid * AutoLotScaleFactor(),
                        InpAutoLotFar * AutoLotScaleFactor(),
                        AutoLotScaleFactor(),
                        InpTargetUSD,
                       InpBasketTakeProfitUSD,
                       (InpUseTrendOneSideGrid ? "on" : "off"),
                       (InpUseFloatReanchor ? "on" : "off"),
                       (InpUseTrendRescueMode ? "on" : "off"),
                       (InpUseTrailing ? "on" : "off"),
                       InpLogLevel,
                       g_moneyScale));

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   double profit = AccountInfoDouble(ACCOUNT_PROFIT);
   double oneLotSpreadCost = EstimateSpreadCost(1.0);
   double voluntaryLossFloor = VoluntaryLossFloorBalance(0.0);
   RefreshPriceScaleCacheIfNeeded(true);
   double currentPrice = g_priceScaleCacheMidPrice;
   double currentAtr = g_priceScaleCacheATR;
   double priceScale = g_priceScaleCacheFactor;
   string cycleGridStepSnapshot = (g_cycleGridStepDistance > 0.0
                                   ? DoubleToString(g_cycleGridStepDistance, g_digits)
                                   : "none");

   Log(2, StringFormat("Straddle: account context accountCurrency=%s accountName=\"%s\" accountServer=\"%s\" accountCompany=\"%s\" moneyScale=%.2f scaleReason=%s accountCurrencyDigits=%d balanceRaw=%.2f balanceDisplay=%.2f equityRaw=%.2f equityDisplay=%.2f profitRaw=%.2f profitDisplay=%.2f autoSpread=%s autoCommission=%s oneLotSpreadCost=%.2f",
                       g_accountCurrency, g_accountName, g_accountServer, g_accountCompany,
                       g_moneyScale, g_moneyScaleReason, g_accountCurrencyDigits,
                       balance, AccountMoneyToDisplay(balance),
                       equity, AccountMoneyToDisplay(equity),
                       profit, AccountMoneyToDisplay(profit),
                       (InpUseAutoSpreadCost ? "true" : "false"),
                       (InpUseAutoCommissionEstimate ? "true" : "false"),
                       oneLotSpreadCost));

   Log(2, StringFormat("Straddle: effective money inputs raw/effective target %.2f/%.2f reserve %.2f/%.2f cleanupBuffer %.2f/%.2f commissionPerLot %.2f/%.2f rescueTrigger %.2f/%.2f rescueHarvest %.2f/%.2f trendTarget %.2f/%.2f trendCleanupBuffer %.2f/%.2f entryCleanupBuffer %.2f/%.2f pairMin %.2f/%.2f pairBuffer %.2f/%.2f floatingEquityBuffer %.2f/%.2f staleLargestLossTrigger %.2f/%.2f staleTotalLossTrigger %.2f/%.2f protectedFloor %.2f/%.2f protectedBuffer %.2f/%.2f voluntaryLossFloor %.2f pressureGap %.2f/%.2f moneyGapLotStep %.2f/%.2f pressureExposure %.2f baseExposure %.2f pressureMaxEntries %d totalSafetyMaxEntries %d pressureConfirm bars=%d move=%.2f adaptiveMin %.2f/%.2f adaptiveMax %.2f/%.2f skipDiagGap %.2f/%.2f skipThrottleSec %d",
                       InpTargetUSD, MoneyInput(InpTargetUSD),
                       InpProfitReserveUSD, MoneyInput(InpProfitReserveUSD),
                       InpCleanupCostBufferUSD, MoneyInput(InpCleanupCostBufferUSD),
                       InpCommissionPerLot, CommissionPerLotEffective(),
                       InpRescueHedgeTriggerLossUSD, MoneyInput(InpRescueHedgeTriggerLossUSD),
                       InpRescueHedgeHarvestUSD, MoneyInput(InpRescueHedgeHarvestUSD),
                       InpTrendRescueProfitTargetUSD, MoneyInput(InpTrendRescueProfitTargetUSD),
                       InpTrendRescueCleanupBufferUSD, MoneyInput(InpTrendRescueCleanupBufferUSD),
                        InpTrendRescueEntryCleanupBufferUSD, MoneyInput(InpTrendRescueEntryCleanupBufferUSD),
                        InpPairCleanupMinProfitUSD, MoneyInput(InpPairCleanupMinProfitUSD),
                        InpPairCleanupBufferUSD, MoneyInput(InpPairCleanupBufferUSD),
                        InpFloatingPairCleanupMinEquityBufferUSD, MoneyInput(InpFloatingPairCleanupMinEquityBufferUSD),
                        InpStaleTradeLargestLossTriggerUSD, MoneyInput(InpStaleTradeLargestLossTriggerUSD),
                        InpStaleTradeTotalLossTriggerUSD, MoneyInput(InpStaleTradeTotalLossTriggerUSD),
                        InpProtectedProfitFloorUSD, MoneyInput(InpProtectedProfitFloorUSD),
                        InpProtectedProfitCleanupBufferUSD, MoneyInput(InpProtectedProfitCleanupBufferUSD),
                        voluntaryLossFloor,
                        InpTrendRescuePressureGapUSD, MoneyInput(InpTrendRescuePressureGapUSD),
                        InpTrendRescueMoneyGapLotStepUSD, MoneyInput(InpTrendRescueMoneyGapLotStepUSD),
                        InpTrendRescuePressureExposureRatio, InpTrendRescueExposureRatio,
                        InpTrendRescuePressureMaxEntries, InpTrendRescueTotalSafetyMaxEntries,
                        InpTrendRescuePressureConfirmLookbackBars,
                        InpTrendRescuePressureConfirmMoveUSD,
                        InpTrendRescueMinAdaptiveHarvestUSD, MoneyInput(InpTrendRescueMinAdaptiveHarvestUSD),
                        InpTrendRescueMaxAdaptiveHarvestUSD, MoneyInput(InpTrendRescueMaxAdaptiveHarvestUSD),
                        InpTrendRescueSkipDiagGapUSD, MoneyInput(InpTrendRescueSkipDiagGapUSD),
                        InpTrendRescueSkipLogThrottleSec));

   Log(2, StringFormat("Straddle: stuck recovery cleanup inputs enabled=%s gap %.2f/%.2f balanceCushion %.2f/%.2f spendShare %.2f maxSpend %.2f/%.2f minEquityBuffer %.2f/%.2f maxActions %d",
                       (InpUseStuckRecoveryCleanup ? "true" : "false"),
                       InpStuckRecoveryGapUSD, MoneyInput(InpStuckRecoveryGapUSD),
                       InpStuckRecoveryBalanceCushionUSD, MoneyInput(InpStuckRecoveryBalanceCushionUSD),
                       InpStuckRecoverySpendShare,
                       InpStuckRecoveryMaxSpendUSD, MoneyInput(InpStuckRecoveryMaxSpendUSD),
                       InpStuckRecoveryMinEquityBufferUSD, MoneyInput(InpStuckRecoveryMinEquityBufferUSD),
                       InpStuckRecoveryMaxActionsPerTick));

   Log(2, StringFormat("Straddle: equity pressure inputs enabled=%s dd %.2f/%.2f severe %.2f/%.2f cooldownMultiplier %.2f stepMultiplier %.2f lotMultiplier %.2f disableContinuationOverride=%s severeMaxTrendEntries %d",
                       (InpUseEquityPressureMode ? "true" : "false"),
                       InpEquityPressureDDUSD, MoneyInput(InpEquityPressureDDUSD),
                       InpEquityPressureSevereDDUSD, MoneyInput(InpEquityPressureSevereDDUSD),
                       InpEquityPressureCooldownMultiplier,
                       InpEquityPressureStepMultiplier,
                       InpEquityPressureLotMultiplier,
                       (InpEquityPressureDisableContinuationOverride ? "true" : "false"),
                       InpEquityPressureMaxTrendEntries));

   Log(2, StringFormat("Straddle: floating pair cleanup inputs enabled=%s profitShare %.2f maxProfitTickets %d maxLoserActions %d minMarginLevel %.2f minEquityBuffer %.2f/%.2f reserveExpirySec %d",
                       (InpUseTrendRescueFloatingPairCleanup ? "true" : "false"),
                       InpFloatingPairCleanupProfitShare,
                       InpFloatingPairCleanupMaxProfitTicketsPerTick,
                       InpFloatingPairCleanupMaxLoserActionsPerTick,
                       InpFloatingPairCleanupMinMarginLevelPct,
                       InpFloatingPairCleanupMinEquityBufferUSD,
                       MoneyInput(InpFloatingPairCleanupMinEquityBufferUSD),
                       InpPairCleanupReserveExpirySec));

   Log(2, StringFormat("Straddle: stale cleanup inputs enabled=%s minAgeMinutes %d triggerCount %d largestLossTrigger %.2f/%.2f totalLossTrigger %.2f/%.2f maxLossPerTick %.2f/%.2f maxLossPerHour %.2f/%.2f minEquityDD %.2f/%.2f maxActions %d",
                       (InpUseStaleTradeCleanup ? "true" : "false"),
                       InpStaleTradeMinAgeMinutes,
                       InpStaleTradeTriggerCount,
                       InpStaleTradeLargestLossTriggerUSD, MoneyInput(InpStaleTradeLargestLossTriggerUSD),
                       InpStaleTradeTotalLossTriggerUSD, MoneyInput(InpStaleTradeTotalLossTriggerUSD),
                       InpStaleTradeMaxLossPerTickUSD, MoneyInput(InpStaleTradeMaxLossPerTickUSD),
                       InpStaleTradeMaxLossPerHourUSD, MoneyInput(InpStaleTradeMaxLossPerHourUSD),
                       InpStaleTradeMinEquityDDUSD, MoneyInput(InpStaleTradeMinEquityDDUSD),
                       InpStaleTradeMaxActionsPerTick));

   Log(2, StringFormat("Straddle: adaptive price scale enabled=%s mode=%d currentPrice=%s referencePrice=%.2f atr=%s referenceATR=%.2f scaleFactor=%.4f bounds %.2f..%.2f raw/effective gridStep %.2f/%s cycleSnapshot %s trendStep %.2f/%s minTrendStep %.2f/%s pressureConfirmMove %.2f/%s recoveryDirectionMove %.2f/%s",
                       (InpUseAdaptivePriceScale ? "true" : "false"),
                       InpPriceScaleMode,
                       DoubleToString(currentPrice, g_digits),
                       InpPriceScaleReferencePrice,
                       DoubleToString(currentAtr, g_digits),
                       InpPriceScaleReferenceATR,
                       priceScale,
                       InpPriceScaleMin,
                       InpPriceScaleMax,
                       InpGridStepUSD, DoubleToString(PriceDistanceInput(InpGridStepUSD), g_digits),
                       cycleGridStepSnapshot,
                       InpTrendRescueStepUSD, DoubleToString(PriceDistanceInput(InpTrendRescueStepUSD), g_digits),
                       InpTrendRescueMinStepUSD, DoubleToString(PriceDistanceInput(InpTrendRescueMinStepUSD), g_digits),
                       InpTrendRescuePressureConfirmMoveUSD, DoubleToString(PriceDistanceInput(InpTrendRescuePressureConfirmMoveUSD), g_digits),
                       InpRecoveryDirectionMinMoveUSD, DoubleToString(PriceDistanceInput(InpRecoveryDirectionMinMoveUSD), g_digits)));
  }

//+------------------------------------------------------------------+
//| Throttled debug log for strict fixed-slot skips.                  |
//+------------------------------------------------------------------+
void LogFixedSlotSkip(const string msg)
  {
   if((TimeCurrent() - g_lastSlotSkipLog) < InpRetrySeconds)
      return;
   g_lastSlotSkipLog = TimeCurrent();
   Log(2, msg);
  }

//+------------------------------------------------------------------+
//| Per-instance terminal global-variable name that persists the      |
//| teardown intent across recompile/VPS restart. Distinguishes an    |
//| interrupted teardown (marker present -> resume close-out) from a  |
//| normal freshly-armed grid (no marker -> leave it alone), since    |
//| both look identical on disk (positions==0 && pendings>0).         |
//| 0.0.5: scoped per ACCOUNT LOGIN + magic + symbol so a marker from |
//| another account on the same terminal can never leak in, and the   |
//| stored VALUE is the teardown start time (freshness stamp).        |
//| Strategy Tester: global variables are cleared per run - fine, no  |
//| restart occurs within a single test; live terminals persist them. |
//+------------------------------------------------------------------+
string TDVar()
  {
   return StringFormat("Straddle_TD_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

// 1.1.19: pending "next day lot x2" after daily loss limit (restart-safe)
string DL2Var()
  {
   return StringFormat("Straddle_DL2_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

// 1.1.20: day-start equity + day stamp (restart mid-day keeps equity anchor)
string DSEVar()
  {
   return StringFormat("Straddle_DSE_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }
string DSDVar()
  {
   return StringFormat("Straddle_DSD_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

// 1.1.23: persisted daily-stop marker. Unlike the generic teardown marker,
// this explicitly means "force-close the matching book" and must survive a
// VPS restart or server-day rollover until all positions and pendings are gone.
string DLSVar()
  {
   return StringFormat("Straddle_DLS_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

// 1.1.35: multi-day no-trade resume day after HARD daily LOSS limit (restart-safe)
string DLCVar()
  {
   return StringFormat("Straddle_DLC_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

void PersistPendingNextDayLot2x(const bool pending)
  {
   if(pending)
     {
      if(!GlobalVariableSet(DL2Var(), 1.0))
         Log(0, "Straddle: WARN could not set next-day lot2x marker");
     }
   else
      GlobalVariableDel(DL2Var());
  }

void LoadPendingNextDayLot2x()
  {
   g_pendingNextDayLot2x = (GlobalVariableCheck(DL2Var()) &&
                            GlobalVariableGet(DL2Var()) > 0.5);
  }

//+------------------------------------------------------------------+
//| 1.1.35: clamp selected no-trade days to 1..4                      |
//+------------------------------------------------------------------+
int DailyLossNoTradeDays()
  {
   int days = (int)InpDailyLossNoTradeDays;
   if(days < 1)
      days = 1;
   if(days > 4)
      days = 4;
   return days;
  }

//+------------------------------------------------------------------+
//| 1.1.35: add N whole server days to a day-00:00 stamp              |
//+------------------------------------------------------------------+
datetime AddServerDays(const datetime day0, const int days)
  {
   if(days <= 0)
      return DailyDayStamp(day0);
   return DailyDayStamp(day0 + (datetime)days * 86400);
  }

bool IsLossCooldownActive(const datetime day0)
  {
   return (g_lossCooldownResumeDay > 0 && day0 < g_lossCooldownResumeDay);
  }

void PersistLossCooldownResumeDay()
  {
   if(g_lossCooldownResumeDay <= 0)
     {
      GlobalVariableDel(DLCVar());
      return;
     }
   if(!GlobalVariableSet(DLCVar(), (double)g_lossCooldownResumeDay))
      Log(0, "Straddle: WARN could not set daily-loss no-trade cooldown marker");
  }

void ClearLossCooldown()
  {
   g_lossCooldownResumeDay = 0;
   GlobalVariableDel(DLCVar());
  }

void LoadLossCooldown()
  {
   g_lossCooldownResumeDay = 0;
   if(!GlobalVariableCheck(DLCVar()))
      return;
   const double raw = GlobalVariableGet(DLCVar());
   if(!MathIsValidNumber(raw) || raw <= 0.0)
     {
      GlobalVariableDel(DLCVar());
      return;
     }
   g_lossCooldownResumeDay = DailyDayStamp((datetime)raw);
  }

//+------------------------------------------------------------------+
//| 1.1.35: arm multi-day no-trade window after HARD daily LOSS.      |
//| Resume day = hit day + N server days (N=1..4).                    |
//| Example N=1: stop today, resume tomorrow.                         |
//| Example N=2: stop today + tomorrow, resume day after.             |
//+------------------------------------------------------------------+
void ArmDailyLossNoTradeCooldown(const datetime day0)
  {
   const int n = DailyLossNoTradeDays();
   g_lossCooldownResumeDay = AddServerDays(day0, n);
   PersistLossCooldownResumeDay();
   LogAlways(StringFormat(
      "Straddle: DAILY LOSS NO-TRADE - do not trade for %d day(s); resume on server day %s (stamp=%I64d)",
      n,
      TimeToString(g_lossCooldownResumeDay, TIME_DATE),
      (long)g_lossCooldownResumeDay));
  }

//+------------------------------------------------------------------+
//| 1.1.35: keep or clear multi-day cooldown around day boundaries.   |
//| Returns true when new trading must stay blocked for the cooldown. |
//+------------------------------------------------------------------+
bool ApplyLossCooldownForDay(const datetime day0, const bool logTransition)
  {
   LoadLossCooldown();
   if(g_lossCooldownResumeDay <= 0)
      return false;

   if(day0 < g_lossCooldownResumeDay)
     {
      g_dailyTradingStopped = true;
      // Force-close is only needed while residual exposure still exists;
      // multi-day cooldown after flat is a "no new trade" gate only.
      if((CountMyPositions() + CountMyPendings()) <= 0)
         g_dailyStopForceClose = false;
      g_dailyStopReason = StringFormat(
         "daily loss no-trade cooldown (%d day setting) until %s",
         DailyLossNoTradeDays(),
         TimeToString(g_lossCooldownResumeDay, TIME_DATE));
      if(logTransition)
         LogAlways(StringFormat(
            "Straddle: DAILY LOSS NO-TRADE ACTIVE - blocked until %s (dayPL=%.2f)",
            TimeToString(g_lossCooldownResumeDay, TIME_DATE),
            g_dailyEquityPL));
      return true;
     }

   // Resume day reached: clear marker and allow normal day start.
   if(logTransition)
      LogAlways(StringFormat(
         "Straddle: DAILY LOSS NO-TRADE ENDED - trading may resume (was blocked until %s)",
         TimeToString(g_lossCooldownResumeDay, TIME_DATE)));
   ClearLossCooldown();
   return false;
  }

bool HasPersistedDailyStopMarker()
  {
   if(!GlobalVariableCheck(DLSVar()))
      return false;
   double marker = GlobalVariableGet(DLSVar());
   if(!MathIsValidNumber(marker) || marker <= 0.0)
     {
      GlobalVariableDel(DLSVar());
      return false;
     }
   return true;
  }

void PersistDailyStopMarker()
  {
   datetime stamp = (g_dailyDayStamp > 0 ? g_dailyDayStamp : TimeCurrent());
   if(!GlobalVariableSet(DLSVar(), (double)stamp))
      Log(0, "Straddle: WARN could not set persisted daily-stop marker");
  }

void ClearPersistedDailyStopMarker()
  {
   GlobalVariableDel(DLSVar());
  }

void PersistDayStartEquity()
  {
   if(!GlobalVariableSet(DSDVar(), (double)g_dailyDayStamp))
      Log(0, "Straddle: WARN could not set day-stamp marker");
   if(!GlobalVariableSet(DSEVar(), g_dayStartEquity))
      Log(0, "Straddle: WARN could not set day-start equity marker");
  }

// Load day-start equity if same server day; else open a new day anchor at current equity.
void LoadOrInitDayStartEquity(const datetime day0)
  {
   if(GlobalVariableCheck(DSDVar()) && GlobalVariableCheck(DSEVar()))
     {
      datetime storedDay = (datetime)GlobalVariableGet(DSDVar());
      double   storedEq  = GlobalVariableGet(DSEVar());
      if(storedDay == day0 && storedEq > 0.0)
        {
         g_dailyDayStamp  = day0;
         g_dayStartEquity = storedEq;
         return;
        }
     }
   g_dailyDayStamp  = day0;
   g_dayStartEquity = AccountInfoDouble(ACCOUNT_EQUITY);
   PersistDayStartEquity();
  }

//+------------------------------------------------------------------+
//| Per-instance terminal global-variable name for rescue hold.       |
//| Rescue is separate from teardown: it persists the intent to delete|
//| pendings only and hold positions until the basket is flat or      |
//| safely positive enough to close without expected balance damage.  |
//+------------------------------------------------------------------+
string RHVar()
  {
   return StringFormat("Straddle_RH_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

//+------------------------------------------------------------------+
//| Per-instance rescue anchor balance. This is the realized-bank     |
//| baseline for rescue cleanup; without it, losing cleanup is denied.|
//+------------------------------------------------------------------+
string RABVar()
  {
   return StringFormat("Straddle_RAB_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

//+------------------------------------------------------------------+
//| Per-instance rescue hedge cooldown timestamp.                     |
//+------------------------------------------------------------------+
string RHTVar()
  {
   return StringFormat("Straddle_RHT_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

//+------------------------------------------------------------------+
//| 0.0.37: Per-instance persisted PEAK account equity. Persisted so a |
//| VPS restart does NOT re-seed peak to post-crash equity. Used for   |
//| monitoring/logging only - the equity backstop arms/releases on the |
//| absolute EquityFloatingDrawdown(), which is restart-robust.        |
//+------------------------------------------------------------------+
string PEQVar()
  {
   return StringFormat("Straddle_PEQ_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

//+------------------------------------------------------------------+
//| Per-instance Trend Rescue Mode markers. Direction and last-entry |
//| metadata keep restart behavior trend-side-only instead of letting |
//| the normal grid repopulate against the break.                     |
//+------------------------------------------------------------------+
string TRVar()
  {
   return StringFormat("Straddle_TR_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

string TRDVar()
  {
   return StringFormat("Straddle_TRD_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

string TRTVar()
  {
   return StringFormat("Straddle_TRT_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

string TRPVar()
  {
   return StringFormat("Straddle_TRP_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

//+------------------------------------------------------------------+
//| Per-instance exact cycle-start marker. This is separate from the |
//| recovered anchor: close/cleanup safety decisions need the true    |
//| realized-P/L history window, not the earliest surviving ticket.   |
//+------------------------------------------------------------------+
string CSVar()
  {
   return StringFormat("Straddle_CS_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

string CGSVar()
  {
   return StringFormat("Straddle_CGS_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

//+------------------------------------------------------------------+
//| Per-instance teardown close-all safety threshold. Different       |
//| teardown reasons use different guards: target teardown is guarded |
//| by the target, while boundary/rescue safety uses the close buffer.|
//+------------------------------------------------------------------+
string TSVar()
  {
   return StringFormat("Straddle_TS_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

//+------------------------------------------------------------------+
//| 0.0.38: Per-instance marker tagging the active teardown as one    |
//| ARMED BY THE BASKET TAKE-PROFIT. Persisted so a VPS restart that  |
//| interrupts a basket teardown RESUMES under the strict floating    |
//| >= +threshold guard rather than the generic (weaker) CycleNet>=0  |
//| guard - this is what keeps the no-loss + green-close invariant    |
//| holding across restart, market-closed resume, and partial close.  |
//+------------------------------------------------------------------+
string BTVar()
  {
   return StringFormat("Straddle_BT_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

//+------------------------------------------------------------------+
//| 0.0.41 FIX C1: per-instance persisted bounded-averaging gate state.|
//| BAPVar = last accepted averaging add fill PRICE (adverse-step       |
//| reference); BATVar = last accepted averaging add TIME (cooldown).   |
//| Persisted so a recompile/VPS restart mid-averaging-episode keeps    |
//| the adverse-step + cooldown gates instead of treating the next add  |
//| as a first-ever add (which would defeat the step gate). Mirrors the |
//| RHTVar / PersistRescueHedgeTime trio. Cleared on cycle-flat so a    |
//| new episode never inherits a stale reference.                       |
//+------------------------------------------------------------------+
string BAPVar()
  {
   return StringFormat("Straddle_BAP_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

string BATVar()
  {
   return StringFormat("Straddle_BAT_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

void PersistCycleStart()
  {
   if(g_cycleStart <= 0)
      return;
   if(!GlobalVariableSet(CSVar(), (double)g_cycleStart))
      Log(0, "Straddle: WARN could not persist cycle start marker");
  }

bool LoadPersistedCycleStart()
  {
   g_cycleStartTrusted = false;
   if(!GlobalVariableCheck(CSVar()))
      return false;

   datetime stamp = (datetime)GlobalVariableGet(CSVar());
   if(stamp <= 0)
     {
      GlobalVariableDel(CSVar());
      g_cycleStart = 0;
      return false;
     }

   g_cycleStart = stamp;
   g_cycleStartTrusted = true;
   Log(1, StringFormat("Straddle: restored exact cycle start marker %s",
                       TimeToString(g_cycleStart, TIME_DATE | TIME_SECONDS)));
   return true;
  }

void ClearPersistedCycleStart()
  {
   GlobalVariableDel(CSVar());
   g_cycleStart = 0;
   g_cycleStartTrusted = false;
  }

void PersistCycleGridStepDistance()
  {
   if(g_cycleGridStepDistance <= 0.0)
      return;
   if(!GlobalVariableSet(CGSVar(), g_cycleGridStepDistance))
      Log(0, "Straddle: WARN could not persist cycle grid step snapshot");
  }

bool LoadPersistedCycleGridStepDistance()
  {
   g_cycleGridStepDistance = 0.0;
   if(!GlobalVariableCheck(CGSVar()))
      return false;

   double step = GlobalVariableGet(CGSVar());
   if(!MathIsValidNumber(step) || step <= 0.0)
     {
      GlobalVariableDel(CGSVar());
      return false;
     }

   g_cycleGridStepDistance = NormalizePriceDistanceToTick(step);
   Log(1, StringFormat("Straddle: restored cycle grid step snapshot rawGridStep=%.2f snapshottedGridStep=%s liveScale=%.4f liveGridStep=%s",
                       InpGridStepUSD,
                       DoubleToString(g_cycleGridStepDistance, g_digits),
                       PriceScaleFactor(),
                       DoubleToString(GridStepDistance(), g_digits)));
   return true;
  }

void ClearPersistedCycleGridStepDistance()
  {
   GlobalVariableDel(CGSVar());
   g_cycleGridStepDistance = 0.0;
  }

void PersistRescueAnchorBalance()
  {
   if(!g_rescueAnchorTrusted || g_rescueAnchorBalance <= 0.0)
      return;
   if(!GlobalVariableSet(RABVar(), g_rescueAnchorBalance))
      Log(0, "Straddle: WARN could not persist rescue anchor balance");
  }

bool LoadRescueAnchorBalance()
  {
   g_rescueAnchorTrusted = false;
   g_rescueAnchorBalance = 0.0;
   if(!GlobalVariableCheck(RABVar()))
      return false;

   double anchorBalance = GlobalVariableGet(RABVar());
   if(anchorBalance <= 0.0)
     {
      GlobalVariableDel(RABVar());
      return false;
     }

   g_rescueAnchorBalance = anchorBalance;
   g_rescueAnchorTrusted = true;
   return true;
  }

void ClearRescueAnchorBalance()
  {
   GlobalVariableDel(RABVar());
   g_rescueAnchorBalance = 0.0;
   g_rescueAnchorTrusted = false;
  }

void PersistRescueHedgeTime()
  {
   if(g_lastRescueHedgeTime <= 0)
      return;
   if(!GlobalVariableSet(RHTVar(), (double)g_lastRescueHedgeTime))
      Log(0, "Straddle: WARN could not persist rescue hedge cooldown timestamp");
  }

void LoadRescueHedgeTime()
  {
   g_lastRescueHedgeTime = 0;
   if(!GlobalVariableCheck(RHTVar()))
      return;
   datetime stamp = (datetime)GlobalVariableGet(RHTVar());
   if(stamp <= 0)
      GlobalVariableDel(RHTVar());
   else
      g_lastRescueHedgeTime = stamp;
  }

void ClearRescueHedgeTime()
  {
   GlobalVariableDel(RHTVar());
   g_lastRescueHedgeTime = 0;
  }

//+------------------------------------------------------------------+
//| 0.0.41 FIX C1: persist / load / clear the bounded-averaging gate   |
//| state (last add price + time). Mirrors PersistRescueHedgeTime /     |
//| LoadRescueHedgeTime / ClearRescueHedgeTime. LoadBasketAvgState only  |
//| overwrites the in-memory globals when a positive stored value       |
//| exists; a non-positive/absent stored value leaves the default       |
//| (0.0 / 0) untouched so a first-ever add behaves exactly as before.  |
//+------------------------------------------------------------------+
void PersistBasketAvgState()
  {
   if(g_basketAvgLastEntryPrice > 0.0)
      if(!GlobalVariableSet(BAPVar(), g_basketAvgLastEntryPrice))
         Log(0, "Straddle: WARN could not persist basket averaging last entry price");
   if(g_lastBasketAvgEntryTime > 0)
      if(!GlobalVariableSet(BATVar(), (double)g_lastBasketAvgEntryTime))
         Log(0, "Straddle: WARN could not persist basket averaging last entry time");
  }

void LoadBasketAvgState()
  {
   if(GlobalVariableCheck(BAPVar()))
     {
      double p = GlobalVariableGet(BAPVar());
      if(p > 0.0)
         g_basketAvgLastEntryPrice = p;
      else
         GlobalVariableDel(BAPVar());
     }
   if(GlobalVariableCheck(BATVar()))
     {
      datetime t = (datetime)GlobalVariableGet(BATVar());
      if(t > 0)
         g_lastBasketAvgEntryTime = t;
      else
         GlobalVariableDel(BATVar());
     }
  }

void ClearBasketAvgState()
  {
   GlobalVariableDel(BAPVar());
   GlobalVariableDel(BATVar());
   g_basketAvgLastEntryPrice = 0.0;
   g_lastBasketAvgEntryTime  = 0;
  }

//+------------------------------------------------------------------+
//| 0.0.37: persist / load the peak-equity high-water mark. On load,  |
//| if absent it is seeded to current equity (NOT to a stale post-    |
//| crash low). It is informational only - the breaker arms/releases  |
//| on absolute EquityFloatingDrawdown(balance-equity), so a restart  |
//| cannot mis-fire the backstop even if the peak reload were missed. |
//+------------------------------------------------------------------+
void PersistPeakEquity()
  {
   if(g_peakEquity <= 0.0)
      return;
   if(!GlobalVariableSet(PEQVar(), g_peakEquity))
      Log(0, "Straddle: WARN could not persist peak equity high-water mark");
  }

void LoadPeakEquity()
  {
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   if(GlobalVariableCheck(PEQVar()))
     {
      double stored = GlobalVariableGet(PEQVar());
      if(stored > 0.0)
        {
         g_peakEquity = stored;
         return;
        }
      GlobalVariableDel(PEQVar());
     }
   g_peakEquity = MathMax(0.0, equity);
  }

//+------------------------------------------------------------------+
//| 0.0.37: refresh the peak-equity high-water mark and persist only  |
//| when it grows. Restart-robust seed handled by LoadPeakEquity.     |
//+------------------------------------------------------------------+
void UpdatePeakEquity()
  {
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   if(equity > g_peakEquity)
     {
      g_peakEquity = equity;
      PersistPeakEquity();
     }
  }

bool IsTrendRescueActive()
  {
   return g_trendRescueActive;
  }

void PersistTrendRescueState()
  {
   if(!g_trendRescueActive)
      return;
   if(!GlobalVariableSet(TRVar(), (double)TimeCurrent()))
      Log(0, "Straddle: WARN could not persist trend rescue marker");
   if(!GlobalVariableSet(TRDVar(), (double)g_trendRescueDirection))
      Log(0, "Straddle: WARN could not persist trend rescue direction");
   if(g_lastTrendRescueEntryTime > 0)
     {
      if(!GlobalVariableSet(TRTVar(), (double)g_lastTrendRescueEntryTime))
         Log(0, "Straddle: WARN could not persist trend rescue cooldown timestamp");
     }
   else
      GlobalVariableDel(TRTVar());

   if(g_trendRescueLastEntryPrice > 0.0)
     {
      if(!GlobalVariableSet(TRPVar(), g_trendRescueLastEntryPrice))
         Log(0, "Straddle: WARN could not persist trend rescue last-entry price");
     }
   else
      GlobalVariableDel(TRPVar());
  }

bool LoadTrendRescueState()
  {
   g_trendRescueActive = false;
   g_trendRescueDirection = 0;
   g_lastTrendRescueDirectionSwitchTime = 0;
   g_lastTrendRescueEntryTime = 0;
   g_trendRescueLastEntryPrice = 0.0;

   if(!GlobalVariableCheck(TRVar()))
      return false;

   int direction = 0;
   if(GlobalVariableCheck(TRDVar()))
      direction = (int)GlobalVariableGet(TRDVar());
   if(direction != 1 && direction != -1)
     {
      GlobalVariableDel(TRVar());
      GlobalVariableDel(TRDVar());
      GlobalVariableDel(TRTVar());
      GlobalVariableDel(TRPVar());
      return false;
     }

   if(GlobalVariableCheck(TRTVar()))
     {
      datetime stamp = (datetime)GlobalVariableGet(TRTVar());
      if(stamp > 0)
         g_lastTrendRescueEntryTime = stamp;
      else
         GlobalVariableDel(TRTVar());
     }

   if(GlobalVariableCheck(TRPVar()))
     {
      double price = GlobalVariableGet(TRPVar());
      if(price > 0.0)
         g_trendRescueLastEntryPrice = price;
      else
         GlobalVariableDel(TRPVar());
     }

   g_trendRescueActive = true;
   g_trendRescueDirection = direction;
   return true;
  }

void ClearTrendRescueState(const bool clearSharedAnchor)
  {
   g_trendRescueActive = false;
   g_trendRescueDirection = 0;
   g_lastTrendRescueDirectionSwitchTime = 0;
   g_lastTrendRescueEntryTime = 0;
   g_trendRescueLastEntryPrice = 0.0;
   ClearDeferredPairProfitReserve("trend_rescue_state_clear");
   GlobalVariableDel(TRVar());
   GlobalVariableDel(TRDVar());
   GlobalVariableDel(TRTVar());
   GlobalVariableDel(TRPVar());
   if(clearSharedAnchor)
      ClearRescueAnchorBalance();
  }

void PersistTearDownThreshold()
  {
   if(!GlobalVariableSet(TSVar(), MathMax(0.0, g_teardownSafeThreshold)))
      Log(0, "Straddle: WARN could not persist teardown safety threshold");
  }

bool LoadPersistedTearDownThreshold()
  {
   if(!GlobalVariableCheck(TSVar()))
      return false;
   g_teardownSafeThreshold = MathMax(0.0, GlobalVariableGet(TSVar()));
   return true;
  }

void ClearPersistedTearDownThreshold()
  {
   GlobalVariableDel(TSVar());
   g_teardownSafeThreshold = 0.0;
  }

//+------------------------------------------------------------------+
//| 0.0.38: persist / load / clear the basket-teardown tag.           |
//| Persisting it means a restart mid-basket-teardown stays under the |
//| floating>=+threshold resume guard (see ProcessTearDown), never    |
//| downgrading to the generic CycleNet>=0 guard.                     |
//+------------------------------------------------------------------+
void PersistBasketTearDownTag()
  {
   if(!GlobalVariableSet(BTVar(), (double)TimeCurrent()))
      Log(0, "Straddle: WARN could not persist basket take-profit teardown tag");
  }

bool LoadPersistedBasketTearDownTag()
  {
   return GlobalVariableCheck(BTVar());
  }

void ClearPersistedBasketTearDownTag()
  {
   GlobalVariableDel(BTVar());
   g_basketTearDown = false;
  }

bool IsRescueHoldActive()
  {
   return g_rescueHolding;
  }

// 0.0.35: state-agnostic "stuck net exposure with floating loss past trigger".
// True in ANY EA state (including a pure NORMAL CYCLE) when there is real,
// magic+symbol-scoped net exposure AND the rescue-hedge loss trigger has fired.
// NetExposureLots/RescueHedgeLossTriggered already INCLUDE any STR RHG hedge in
// their sums, so once a hedge offsets net lots toward zero this naturally goes
// false (self-stabilizing - no runaway). Self-gates on InpUseRescueHedge first.
// NetExposureLots (defined later) and RescueHedgeLossTriggered (defined later)
// are file-scope functions; MQL5 resolves the forward references within this
// single compilation unit.
bool StuckExposureHedgeEligible()
  {
   if(!InpUseRescueHedge)
      return false;
   double buyLots = 0.0, sellLots = 0.0;
   NetExposureLots(buyLots, sellLots);          // magic+symbol scoped; includes hedge
   double netLots = buyLots - sellLots;
   if(MathAbs(netLots) <= 1.0e-8)               // flat / balanced -> nothing to hedge
      return false;
   if(!RescueHedgeLossTriggered())              // floating<=-trigger OR cycleNet<=-trigger
      return false;
   return true;
  }

// 0.0.34: rescue hedge is permitted in BOTH rescue-hold and active trend
// rescue. Centralizes the entry-gate change in one predicate; rescue-hold
// behavior is preserved because g_rescueHolding alone still satisfies it.
// 0.0.35: third disjunct makes the hedge reachable in a normal cycle exactly
// when there is real net exposure AND floating loss past the trigger. The first
// two disjuncts still fire first, so rescue-hold/trend-rescue behavior is
// unchanged.
bool RescueHedgeStateActive()
  {
   return (g_rescueHolding || g_trendRescueActive || StuckExposureHedgeEligible());
  }

//--- Retcode classification (spec table 4.3 + caveat additions) -----
enum ENUM_RETCODE_ACTION
  {
   ACT_SUCCESS,        // request done / order placed
   ACT_RETRY_REFRESH,  // retry with refreshed market price
   ACT_BACKOFF,        // sleep, then retry
   ACT_ABORT_CYCLE,    // hard limit hit - stop placing more orders (degrade)
   ACT_SKIP_WAIT,      // trading unavailable - skip op, try again on a later tick
   ACT_FAIL            // unexpected - give up on this operation
  };

//+------------------------------------------------------------------+
//| Map a trade server return code to a handling action               |
//+------------------------------------------------------------------+
ENUM_RETCODE_ACTION ClassifyRetcode(const uint rc)
  {
   switch(rc)
     {
      case TRADE_RETCODE_DONE:              // 10009
      case TRADE_RETCODE_PLACED:            // 10008
         return ACT_SUCCESS;

      case 0:                              // 1.1.7: empty/tester-jam result — NEVER hard-fail-spam
         return ACT_BACKOFF;

      case TRADE_RETCODE_REQUOTE:           // 10004
      case TRADE_RETCODE_INVALID_PRICE:     // 10015
      case TRADE_RETCODE_PRICE_CHANGED:     // 10020
      case TRADE_RETCODE_PRICE_OFF:         // 10021
         return ACT_RETRY_REFRESH;

      case TRADE_RETCODE_TOO_MANY_REQUESTS: // 10024
      case TRADE_RETCODE_LOCKED:            // 10028
      case TRADE_RETCODE_FROZEN:            // 10029
      case TRADE_RETCODE_CONNECTION:        // 10031
      case TRADE_RETCODE_TIMEOUT:           // 10012
         return ACT_BACKOFF;

      case TRADE_RETCODE_NO_MONEY:          // 10019
      case TRADE_RETCODE_LIMIT_ORDERS:      // 10033 - pending-order limit
      case TRADE_RETCODE_LIMIT_VOLUME:      // 10034 - symbol volume limit
      case TRADE_RETCODE_LIMIT_POSITIONS:   // 10040 - position-count limit
         return ACT_ABORT_CYCLE;

      case TRADE_RETCODE_TRADE_DISABLED:    // 10017
      case TRADE_RETCODE_MARKET_CLOSED:     // 10018
      case TRADE_RETCODE_CLIENT_DISABLES_AT:// 10027 - autotrading off in terminal
      case TRADE_RETCODE_SERVER_DISABLES_AT:// 10026 - autotrading off by server
         return ACT_SKIP_WAIT;

      default:                              // incl. 10013, 10014, 10044, 10045 - non-retryable
         return ACT_FAIL;
     }
  }

//+------------------------------------------------------------------+
//| True if any trade session is open at secOfDay on the given day    |
//| (session times are seconds from day start; handles overnight wrap)|
//+------------------------------------------------------------------+
bool SessionOpenAt(const ENUM_DAY_OF_WEEK day, const long secOfDay)
  {
   datetime from = 0, to = 0;
   for(int i = 0; SymbolInfoSessionTrade(g_sym, day, i, from, to); i++)
     {
      long f = (long)from, t = (long)to;
      if(t <= f)
         t += 86400;                       // session wraps past midnight
      if(secOfDay >= f && secOfDay < t)
         return true;
     }
   return false;
  }

//+------------------------------------------------------------------+
//| Current time inside an open trade session?                        |
//| If the broker publishes NO session data at all, allow and rely on |
//| the retcode back-off as the safety net (spec fallback).           |
//+------------------------------------------------------------------+
bool InTradeSession()
  {
   bool anyData = false;
   datetime from = 0, to = 0;
   for(int d = SUNDAY; d <= SATURDAY && !anyData; d++)
      if(SymbolInfoSessionTrade(g_sym, (ENUM_DAY_OF_WEEK)d, 0, from, to))
         anyData = true;
   if(!anyData)
      return true;                          // API empty/unavailable - fall back to allowing

   MqlDateTime now;
   TimeToStruct(TimeCurrent(), now);
   long sod = (long)now.hour * 3600 + (long)now.min * 60 + now.sec;
   if(SessionOpenAt((ENUM_DAY_OF_WEEK)now.day_of_week, sod))
      return true;
   // a session from the previous day may wrap past midnight into today
   ENUM_DAY_OF_WEEK prev = (ENUM_DAY_OF_WEEK)((now.day_of_week + 6) % 7);
   return SessionOpenAt(prev, sod + 86400);
  }

//+------------------------------------------------------------------+
//| Tradeability gate - must pass before any order operation.         |
//| Returns false WITHOUT sending any order when the market is closed |
//| or trading is disabled at any layer.                              |
//+------------------------------------------------------------------+
bool CanTrade()
  {
   if(IsStopped())
      return false;
   if((ENUM_SYMBOL_TRADE_MODE)SymbolInfoInteger(g_sym, SYMBOL_TRADE_MODE) != SYMBOL_TRADE_MODE_FULL)
      return false;
   if(TerminalInfoInteger(TERMINAL_TRADE_ALLOWED) == 0)
      return false;
   if(MQLInfoInteger(MQL_TRADE_ALLOWED) == 0)
      return false;
   if(AccountInfoInteger(ACCOUNT_TRADE_ALLOWED) == 0)
      return false;
   return InTradeSession();
  }

bool MarketValidationWeekdaySendAllowed()
  {
   if(!InpUseMarketValidationSafety)
      return true;

   MqlDateTime now;
   TimeToStruct(TimeCurrent(), now);
   if(now.day_of_week == SATURDAY || now.day_of_week == SUNDAY)
      return false;

   return true;
  }

bool MarketValidationFinalSendAllowed(const string context)
  {
   if(IsStopped())
      return false;

   if(!MarketValidationWeekdaySendAllowed())
     {
      Log(2, StringFormat("Straddle: %s send skipped - weekend/server closed-market guard", context));
      return false;
     }

   if(!CanTrade())
     {
      Log(2, StringFormat("Straddle: %s send skipped - tradeability guard", context));
      return false;
     }

   return true;
  }

bool IsHedgingAccount()
  {
   return ((ENUM_ACCOUNT_MARGIN_MODE)AccountInfoInteger(ACCOUNT_MARGIN_MODE)
           == ACCOUNT_MARGIN_MODE_RETAIL_HEDGING);
  }

//--- v1.0.4: symbol guard helper - true only for EUR/USD (suffix-tolerant via base/profit currency)
bool IsEurUsdSymbol(const string sym)
  {
   string base   = SymbolInfoString(sym, SYMBOL_CURRENCY_BASE);
   string profit = SymbolInfoString(sym, SYMBOL_CURRENCY_PROFIT);
   return (base == "EUR" && profit == "USD");
  }

//+------------------------------------------------------------------+
//| Tick size with fallback (spec d: guard tick > 0)                  |
//+------------------------------------------------------------------+
double TickSize()
  {
   double tick = SymbolInfoDouble(g_sym, SYMBOL_TRADE_TICK_SIZE);
   if(tick <= 0.0)
      tick = SymbolInfoDouble(g_sym, SYMBOL_POINT);  // documented fallback
   if(tick <= 0.0)
      tick = 0.01;                                   // last-resort guard (never divide by 0)
   return tick;
  }

//--- Price snapping: ALWAYS to tick size, never to point ------------
double SnapPrice(const double p) { double t = TickSize(); return MathRound(p / t) * t; }
double SnapUp(const double p)    { double t = TickSize(); return MathCeil (p / t) * t; }
double SnapDown(const double p)  { double t = TickSize(); return MathFloor(p / t) * t; }

bool SameTickPrice(const double a, const double b)
  {
   double tick = TickSize();
   return (MathAbs(SnapPrice(a) - SnapPrice(b)) <= tick * 0.5);
  }

double CurrentMidPrice()
  {
   double bid = 0.0, ask = 0.0;
   MqlTick tick;
   if(SymbolInfoTick(g_sym, tick))
     {
      bid = tick.bid;
      ask = tick.ask;
     }

   if(bid <= 0.0)
      bid = SymbolInfoDouble(g_sym, SYMBOL_BID);
   if(ask <= 0.0)
      ask = SymbolInfoDouble(g_sym, SYMBOL_ASK);

   if(bid > 0.0 && ask > 0.0)
      return (bid + ask) * 0.5;
   if(ask > 0.0)
      return ask;
   if(bid > 0.0)
      return bid;
   if(g_anchor > 0.0)
      return g_anchor;
   return MathMax(0.0, InpPriceScaleReferencePrice);
  }

double ATRPriceDistance()
  {
   int period = InpPriceScaleATRPeriod;
   if(period < 1)
      period = 1;

   ENUM_TIMEFRAMES timeframe = InpPriceScaleATRTimeframe;
   // 0.0.40 O3: persist the ATR handle instead of create+release on every call.
   // period/timeframe derive from the constant InpPriceScaleATRPeriod/
   // InpPriceScaleATRTimeframe inputs and g_sym is fixed at runtime, so a single
   // cached handle is always for the same (symbol,timeframe,period). CopyBuffer
   // STILL runs every call so the returned forming-bar ATR is bit-identical to
   // 0.0.39; only the handle create/destroy churn is removed.
   if(g_atrHandle == INVALID_HANDLE)
      g_atrHandle = iATR(g_sym, timeframe, period);
   if(g_atrHandle != INVALID_HANDLE)
     {
      double buffer[];
      ArraySetAsSeries(buffer, true);
      int copied = CopyBuffer(g_atrHandle, 0, 0, 1, buffer);
      if(copied > 0 && ArraySize(buffer) > 0 && buffer[0] > 0.0)
         return buffer[0];
     }

   double totalRange = 0.0;
   int ranges = 0;
   for(int shift = 1; shift <= period; shift++)
     {
      double high = iHigh(g_sym, timeframe, shift);
      double low = iLow(g_sym, timeframe, shift);
      if(high <= 0.0 || low <= 0.0 || high < low)
         continue;

      double trueRange = high - low;
      double previousClose = iClose(g_sym, timeframe, shift + 1);
      if(previousClose > 0.0)
         trueRange = MathMax(trueRange,
                             MathMax(MathAbs(high - previousClose),
                                     MathAbs(low - previousClose)));
      if(trueRange <= 0.0)
         continue;
      totalRange += trueRange;
      ranges++;
     }

   if(ranges > 0 && totalRange > 0.0)
      return totalRange / (double)ranges;
   return MathMax(TickSize(), InpPriceScaleReferenceATR);
  }

double NormalizePriceDistanceToTick(const double rawDistance)
  {
   if(!MathIsValidNumber(rawDistance) || rawDistance<0.0)
     {
      LogOp("Straddle: REJECT_INVALID_INPUT runtime price distance is non-finite or negative; using 0");
      return 0.0;
     }
   if(rawDistance <= 0.0)
      return NormalizeDouble(rawDistance, g_digits);

   double tick = TickSize();
   double distance = rawDistance;
   if(MathIsValidNumber(tick) && tick>0.0)
     {
      const double units=distance/tick;
      if(MathIsValidNumber(units) && units>=0.0)
        {
         const double rounded=MathRound(units)*tick;
         if(MathIsValidNumber(rounded) && rounded>=0.0)
            distance=MathMax(tick,rounded);
        }
     }
   if(!MathIsValidNumber(distance) || distance<0.0)
     {
      LogOp("Straddle: REJECT_INVALID_INPUT runtime normalized price distance invalid; using 0");
      return 0.0;
     }
   const double normalized=NormalizeDouble(distance,g_digits);
   if(!MathIsValidNumber(normalized) || normalized<0.0)
     {
      LogOp("Straddle: REJECT_INVALID_INPUT runtime final price distance invalid; using 0");
      return 0.0;
     }
   return normalized;
  }

bool IsFiatCurrencyCode(const string currency)
  {
   const string code=UpperCopy(currency);
   return (code=="USD" || code=="EUR" || code=="GBP" || code=="JPY" ||
           code=="CHF" || code=="AUD" || code=="CAD" || code=="NZD" ||
           code=="SGD" || code=="HKD" || code=="CNH" || code=="CNY" ||
           code=="NOK" || code=="SEK" || code=="DKK" || code=="PLN" ||
           code=="CZK" || code=="HUF" || code=="TRY" || code=="ZAR" ||
           code=="MXN" || code=="RUB");
  }

bool IsKnownFiatFxSymbol()
  {
   const string base=UpperCopy(SymbolInfoString(g_sym,SYMBOL_CURRENCY_BASE));
   const string profit=UpperCopy(SymbolInfoString(g_sym,SYMBOL_CURRENCY_PROFIT));
   if(base!="" && profit!="" && base!=profit &&
      IsFiatCurrencyCode(base) && IsFiatCurrencyCode(profit))
      return true;

   // Some validator symbol specifications do not expose currency metadata
   // during the first OnInit pass.  Keep suffix-tolerant name fallbacks for
   // the requested EUR, GBP and JPY major pairs.
   const string name=UpperCopy(g_sym);
   return (StringFind(name,"EURUSD")>=0 || StringFind(name,"GBPUSD")>=0 ||
           StringFind(name,"USDJPY")>=0 || StringFind(name,"EURGBP")>=0 ||
           StringFind(name,"EURJPY")>=0 || StringFind(name,"GBPJPY")>=0);
  }

// Detect low-priced symbols before any OnInit validation that can calculate a
// scaled distance.  The Market validator can call OnInit before a first tick
// is available, so SymbolInfoTick/iClose may return no quote.  Currency
// metadata and the symbol-native point/digits provide deterministic fallbacks.
bool DetectLowPricedSymbol()
  {
   const double reference = (InpPriceScaleReferencePrice > 0.0
                             ? InpPriceScaleReferencePrice
                             : 5000.0);
   const double threshold = reference * 0.10;
   const bool knownFx=IsKnownFiatFxSymbol();
   MqlTick tick;
   double price=0.0;
   if(SymbolInfoTick(g_sym,tick))
      price=(tick.bid>0.0 && tick.ask>0.0
             ? (tick.bid+tick.ask)*0.5
             : MathMax(tick.bid,tick.ask));
   if(price <= 0.0)
      price = iClose(g_sym, PERIOD_CURRENT, 0);
   if(price > 0.0)
      return (knownFx && price < threshold);

   const double point = SymbolInfoDouble(g_sym, SYMBOL_POINT);
   const int digits = (int)SymbolInfoInteger(g_sym, SYMBOL_DIGITS);
   return (knownFx && point > 0.0 && digits >= 2 && digits <= 6 && point <= 0.01);
  }

double PriceScaleFactorFrom(const double currentPrice,
                            const double currentAtr)
  {
   // 1.0.4: low-priced symbols (e.g. EURUSD) get a deterministic symbol-native scale so the
   // grid step is LOWPRICE_TARGET_STEP_POINTS points regardless of the XAU-referenced blend
   // (which, with the [0.60,1.80] clamp and the 8.0 ATR fallback, produces absurd ~1.8-unit
   // steps on low-priced symbols). Gold/high-priced symbols are unaffected (gate is false).
   if(g_lowPricedSymbol)
     {
      double lpPoint = SymbolInfoDouble(g_sym, SYMBOL_POINT);
      if(MathIsValidNumber(lpPoint) && lpPoint>0.0 && InpGridStepUSD>0.0)
        {
         const double lpNumerator=(double)LOWPRICE_TARGET_STEP_POINTS*lpPoint;
         double lpScale=(MathIsValidNumber(lpNumerator) ? lpNumerator/InpGridStepUSD : 0.0);
         if(MathIsValidNumber(lpScale) && lpScale > 0.0)
            return lpScale;   // bypass the gold [InpPriceScaleMin,InpPriceScaleMax] clamp
        }
     }

   if(!InpUseAdaptivePriceScale || !InpUsePriceProportionalGrid)
      return 1.0;

   double percentScale = 1.0;
   if(MathIsValidNumber(currentPrice) && currentPrice>0.0 && InpPriceScaleReferencePrice>0.0)
     {
      const double candidate=currentPrice/InpPriceScaleReferencePrice;
      if(MathIsValidNumber(candidate) && candidate>0.0)
         percentScale=candidate;
     }

   double atrScale = 1.0;
   if(MathIsValidNumber(currentAtr) && currentAtr>0.0 && InpPriceScaleReferenceATR>0.0)
     {
      const double candidate=currentAtr/InpPriceScaleReferenceATR;
      if(MathIsValidNumber(candidate) && candidate>0.0)
         atrScale=candidate;
     }

   double scale = 1.0;
   if(InpPriceScaleMode == PRICE_SCALE_PRICE_PERCENT_ONLY)
      scale = percentScale;
   else if(InpPriceScaleMode == PRICE_SCALE_ATR_ONLY)
      scale = atrScale;
   else
     {
      double percentWeight = MathMax(0.0, InpPriceScalePercentWeight);
      double atrWeight = MathMax(0.0, InpPriceScaleATRWeight);
      double weightTotal = percentWeight + atrWeight;
      if(weightTotal > 0.0)
        {
         const double percentTerm=percentScale*percentWeight;
         const double atrTerm=atrScale*atrWeight;
         const double numerator=percentTerm+atrTerm;
         if(MathIsValidNumber(percentTerm) && MathIsValidNumber(atrTerm) &&
            MathIsValidNumber(numerator) && numerator>0.0)
            scale=numerator/weightTotal;
        }
     }

   if(!MathIsValidNumber(scale) || scale <= 0.0)
      scale = 1.0;
   scale = MathMax(InpPriceScaleMin, MathMin(InpPriceScaleMax, scale));
   if(!MathIsValidNumber(scale) || scale<=0.0)
     {
      LogOp("Straddle: REJECT_INVALID_INPUT runtime adaptive price scale invalid; using 1.0");
      return 1.0;
     }
   return scale;
  }

void RefreshPriceScaleCacheIfNeeded(const bool force = false)
  {
   datetime now = TimeCurrent();
   if(!force &&
      g_priceScaleCacheTime == now &&
      g_priceScaleCacheFactor > 0.0)
      return;

   g_priceScaleCacheMidPrice = CurrentMidPrice();
   g_priceScaleCacheATR = ATRPriceDistance();
   g_priceScaleCacheFactor = PriceScaleFactorFrom(g_priceScaleCacheMidPrice,
                                                  g_priceScaleCacheATR);
   g_priceScaleCacheTime = now;
  }

double PriceScaleFactor()
  {
   RefreshPriceScaleCacheIfNeeded();
   return g_priceScaleCacheFactor;
  }

double PriceDistanceInput(const double rawDistance)
  {
   if(!MathIsValidNumber(rawDistance) || rawDistance<0.0)
     {
      LogOp("Straddle: REJECT_INVALID_INPUT runtime distance input invalid; using 0");
      return 0.0;
     }
   if(rawDistance <= 0.0)
      return NormalizePriceDistanceToTick(rawDistance);
   const double factor=PriceScaleFactor();
   if(!MathIsValidNumber(factor) || factor<=0.0)
     {
      LogOp("Straddle: REJECT_INVALID_INPUT runtime distance factor invalid; using unscaled distance");
      return NormalizePriceDistanceToTick(rawDistance);
     }
   const double scaled=rawDistance*factor;
   if(!MathIsValidNumber(scaled) || scaled<0.0)
     {
      LogOp("Straddle: REJECT_INVALID_INPUT runtime distance multiplication overflow; using unscaled distance");
      return NormalizePriceDistanceToTick(rawDistance);
     }
   return NormalizePriceDistanceToTick(scaled);
  }

// 0.0.46: returns true when a strong directional move is detected and InpUseTrendPause=true.
// When false (default), returns immediately with no side effects => byte-identical to 0.0.45.
bool IsTrendPauseActive()
  {
   if(!InpUseTrendPause) return false;
   const int n=InpTrendPauseLookbackBars;
   double c0 = iClose(_Symbol, PERIOD_CURRENT, 0);
   double cN = iClose(_Symbol, PERIOD_CURRENT, n);
   if(c0 <= 0.0 || cN <= 0.0) return false;       // data not ready -> do not pause
   double moved = MathAbs(c0 - cN);
   double thresh = PriceDistanceInput(InpTrendPauseMoveUSD);   // scale the USD threshold the same way grid distances are scaled
   return (moved >= thresh);
  }

//+------------------------------------------------------------------+
//| 1.1.1: trend one-side grid bias.                                  |
//| Returns: 0=no strong trend, +1=uptrend (BUY-only grid),           |
//|          -1=downtrend (SELL-only grid).                           |
//| Uses the same lookback (InpTrendPauseLookbackBars) and move       |
//| threshold (InpTrendPauseMoveUSD via PriceDistanceInput) as        |
//| IsTrendPauseActive. Independent of InpUseTrendPause: one-side can |
//| run even when trend-pause is off.                                 |
//+------------------------------------------------------------------+
// Trend direction independent of one-side grid switch (for double-lot).
// Returns: 0=no trend, +1=up, -1=down.
int DetectTrendDirectionByMove(const double moveUSD)
  {
   const int n = InpTrendPauseLookbackBars;
   if(n < 1)
      return 0;
   double c0 = iClose(_Symbol, PERIOD_CURRENT, 0);
   double cN = iClose(_Symbol, PERIOD_CURRENT, n);
   if(c0 <= 0.0 || cN <= 0.0)
      return 0;
   double moved = MathAbs(c0 - cN);
   double thresh = PriceDistanceInput(moveUSD);
   if(moved < thresh)
      return 0;
   if(c0 > cN)
      return 1;
   return -1;
  }

int DetectTrendDirection()
  {
   return DetectTrendDirectionByMove(InpTrendPauseMoveUSD);
  }

// 1.1.14: easier move threshold for 2x recovery (default $15).
int DetectTrendDirectionForDoubleLot()
  {
   double move = InpTrend2xMoveUSD;
   if(move <= 0.0)
      move = InpTrendPauseMoveUSD;
   return DetectTrendDirectionByMove(move);
  }

int TrendOneSideBias()
  {
   if(!InpUseTrendOneSideGrid)
      return 0;
   return DetectTrendDirection();
  }

// 1.1.12/1.1.14: floating loss deeper than threshold AND trend confirmed.
bool TrendDoubleLotActive()
  {
   if(!InpUseTrendDoubleLot)
      return false;
   double thr = MoneyInput(InpTrendDoubleLotLossUSD);
   if(thr <= 0.0)
      return false;
   double openLots = 0.0;
   double fl = OpenFloatingPL(openLots);
   if(fl > -thr)   // e.g. thr=500 => need fl <= -500
      return false;
   if(DetectTrendDirectionForDoubleLot() == 0)
      return false;
   g_trend2xArmedThisCycle = true; // latch for rest of cycle (fast bank + cleanup)
   return true;
  }

// 1.1.14: recovery mode for this cycle after 2x has armed at least once.
bool Trend2xRecoveryMode()
  {
   if(!InpUseTrendDoubleLot)
      return false;
   return (TrendDoubleLotActive() || g_trend2xArmedThisCycle);
  }

// 1.1.19: recovery day after prior daily-loss stop
bool IsRecoveryLot2xToday()
  {
   return (InpDailyLossNextDayLot2x && g_recoveryLot2xToday);
  }

// Effective cycle bank target.
// 1.1.20: on recovery day lots are x2, so cycle Target is also x2 (e.g. 60 -> 120).
double EffectiveCycleTargetUSD()
  {
   double target = (IsAutoLotMode()
                    ? AutoPercentMoney(InpAutoCycleTargetPercent,
                                       AutoLotBalanceDisplay())
                    : MoneyInput(InpTargetUSD));
   if(target <= 0.0)
      target = MoneyInput(InpTargetUSD);
   if(IsRecoveryLot2xToday())
      target *= 2.0;
   if(InpTrend2xFastBank && g_trend2xArmedThisCycle)
     {
      double fast = MoneyInput(InpTrend2xFastBankUSD);
      if(IsRecoveryLot2xToday())
         fast *= 2.0;
      if(fast > 0.0 && fast < target)
         return fast;
     }
   return target;
  }

// 1.1.23/1.1.36: daily PROFIT and LOSS equity limits.
// Recovery-day x2 applies to lots and cycle target, not to either daily stop.
// Percent path (1.1.34-style): AutoDaily*Percent of day-start equity when
// lot mode is Auto-Balance OR InpDailyLimitsUsePercent=true.
// USD path: InpDailyProfitLimitUSD / InpDailyLossLimitUSD otherwise.
bool UseDailyPercentLimits()
  {
   return (IsAutoLotMode() || InpDailyLimitsUsePercent);
  }

double EffectiveDailyProfitLimitUSD()
  {
   if(UseDailyPercentLimits())
      return AutoPercentMoney(InpAutoDailyProfitPercent,
                              AutoDailyBasisBalanceDisplay());
   return MoneyInput(InpDailyProfitLimitUSD);
  }

double EffectiveDailyLossLimitUSD()
  {
   if(UseDailyPercentLimits())
      return AutoPercentMoney(InpAutoDailyLossPercent,
                              AutoDailyBasisBalanceDisplay());
   // HARD CAP: do not scale with recovery 2x.
   return MoneyInput(InpDailyLossLimitUSD);
  }

// Tier lot, optionally 2x base on recovery day, then trend-side 2x.
double GridLotForLevel(const int level, const bool isBuy)
  {
   double baseLot = TierLot(level);
   // 1.1.19: whole-grid start lots x2 for the recovery day after daily loss limit
   if(IsRecoveryLot2xToday())
      baseLot *= 2.0;

   if(!TrendDoubleLotActive())
      return baseLot;
   int dir = DetectTrendDirectionForDoubleLot();
   // Uptrend: double BUY-stop lots only. Downtrend: double SELL-stop lots only.
   if(dir > 0 && isBuy)
      return baseLot * 2.0;
   if(dir < 0 && !isBuy)
      return baseLot * 2.0;
   return baseLot;
  }

//+------------------------------------------------------------------+
//| 1.1.14: always-visible TREND-2X status (LogAlways, not LogState). |
//| Prints on ON/OFF transition, direction flip, and 5-min heartbeat. |
//+------------------------------------------------------------------+
void LogTrendDoubleLotStatus()
  {
   if(!InpUseTrendDoubleLot)
      return;

   const bool active = TrendDoubleLotActive();
   const int  dir    = (active ? DetectTrendDirectionForDoubleLot() : 0);
   double openLots = 0.0;
   const double fl = OpenFloatingPL(openLots);
   const double thr = MoneyInput(InpTrendDoubleLotLossUSD);
   const datetime now = TimeCurrent();

   // 1.1.15: throttle chatter — only real ON/OFF edges + 5min heartbeat while ON
   const bool becameOn  = (active && !g_trend2xWasActive);
   const bool becameOff = (!active && g_trend2xWasActive);
   const bool dirFlip   = (active && g_trend2xWasActive && dir != 0 && dir != g_trend2xLastDir &&
                           g_trend2xLastLog > 0 && (now - g_trend2xLastLog) >= 60);
   const bool heartbeat = (active && g_trend2xLastLog > 0 && (now - g_trend2xLastLog) >= 300);
   if(!becameOn && !becameOff && !dirFlip && !heartbeat)
     {
      if(active)
        {
         g_trend2xWasActive = true;
         if(dir != 0)
            g_trend2xLastDir = dir;
        }
      return;
     }

   if(active)
     {
      LogAlways(StringFormat(
         "Straddle: TREND-2X ON dir=%s float=%.0f thr=%.0f openLots=%.2f (1x->2x upgrade only; no loser close)%s",
         (dir > 0 ? "UP" : (dir < 0 ? "DOWN" : "?")),
         fl, thr, openLots,
         (heartbeat && !becameOn ? " [hb]" : "")));
     }
   else if(becameOff)
     {
      LogAlways(StringFormat(
         "Straddle: TREND-2X OFF float=%.0f thr=%.0f (back to 1x on new fills)",
         fl, thr));
     }

   g_trend2xWasActive = active;
   g_trend2xLastDir   = dir;
   g_trend2xLastLog   = now;
  }

//+------------------------------------------------------------------+
//| 1.1.14: when TREND-2X is armed, remove with-trend grid pendings   |
//| whose volume is still 1x so PopulateGrid can re-place them at 2x  |
//| FIXED prices. Does NOT touch counter-trend side or open positions.|
//| Throttled to at most one mass-upgrade wave per 15 seconds.        |
//+------------------------------------------------------------------+
int UpgradeTrendDoubleLotPendings()
  {
   if(!InpUseTrendDoubleLot || !TrendDoubleLotActive())
      return 0;
   if(IsStopped() || TradeJamActive())
      return 0;

   const datetime now = TimeCurrent();
   if(g_trend2xLastUpgrade > 0 && (now - g_trend2xLastUpgrade) < 5)
      return 0;

   const int dir = DetectTrendDirectionForDoubleLot();
   if(dir == 0)
      return 0;

   ulong tickets[];
   int   n = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;

      bool isBuy = false;
      int  lvl   = 0;
      if(!ParseLegComment(OrderGetString(ORDER_COMMENT), isBuy, lvl))
         continue;
      if(lvl < 1 || lvl > EffectiveGridLevels())
         continue;

      // with-trend side only
      if(dir > 0 && !isBuy)
         continue;
      if(dir < 0 && isBuy)
         continue;

      const double have = OrderGetDouble(ORDER_VOLUME_CURRENT);
      const double want = NormalizeLot(GridLotForLevel(lvl, isBuy));
      if(want <= 0.0 || have <= 0.0)
         continue;
      // already at (or above) target 2x size
      if(have + 1.0e-8 >= want)
         continue;
      // true upgrade only (want ~2x have); avoid micro float noise
      if(want + 1.0e-8 < have * 1.5)
         continue;

      const int sz = ArraySize(tickets);
      ArrayResize(tickets, sz + 1);
      tickets[sz] = ticket;
      n = sz + 1;
     }

   if(n <= 0)
      return 0;

   int deleted = 0;
   for(int j = 0; j < n; j++)
     {
      if(DeleteOnePending(tickets[j]))
         deleted++;
     }

   g_trend2xLastUpgrade = now;
   if(deleted > 0)
     {
      g_bookDirty = true;
      LogAlways(StringFormat(
         "Straddle: TREND-2X UPGRADE deleted %d undersized with-trend pendings dir=%s (refill at 2x next)",
         deleted, (dir > 0 ? "UP" : "DOWN")));
     }
   return deleted;
  }

double GridStepDistance()
  {
   return PriceDistanceInput(InpGridStepUSD);
  }

double SanitizeCycleGridStepDistance(const double step)
  {
   if(!MathIsValidNumber(step) || step <= 0.0)
      return 0.0;
   return NormalizePriceDistanceToTick(step);
  }

double LegacyCycleGridStepDistance()
  {
   return SanitizeCycleGridStepDistance(InpGridStepUSD);
  }

void AddCycleGridStepRecoverySlot(bool &slotIsBuy[],
                                  int &slotLevel[],
                                  double &slotPrice[],
                                  int &slotCount,
                                  const bool isBuy,
                                  const int level,
                                  const double price)
  {
   if(level < 1 || price <= 0.0)
      return;

   ArrayResize(slotIsBuy, slotCount + 1);
   ArrayResize(slotLevel, slotCount + 1);
   ArrayResize(slotPrice, slotCount + 1);
   slotIsBuy[slotCount] = isBuy;
   slotLevel[slotCount] = level;
   slotPrice[slotCount] = price;
   slotCount++;
  }

void AddCycleGridStepCandidate(double &candidates[],
                               int &candidateCount,
                               const double rawStep)
  {
   double step = SanitizeCycleGridStepDistance(rawStep);
   if(step <= 0.0)
      return;

   ArrayResize(candidates, candidateCount + 1);
   candidates[candidateCount] = step;
   candidateCount++;
  }

double SelectCycleGridStepCandidate(double &candidates[], const int candidateCount)
  {
   if(candidateCount <= 0)
      return 0.0;

   ArraySort(candidates);
   int middle = candidateCount / 2;
   if((candidateCount % 2) == 1)
      return SanitizeCycleGridStepDistance(candidates[middle]);

   return SanitizeCycleGridStepDistance((candidates[middle - 1] + candidates[middle]) * 0.5);
  }

double InferCycleGridStepDistanceFromTickets(const bool pendingOnly)
  {
   bool slotIsBuy[];
   int slotLevel[];
   double slotPrice[];
   int slotCount = 0;

   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;
      bool isBuy; int level;
      if(!ParseLegComment(OrderGetString(ORDER_COMMENT), isBuy, level))
         continue;
      AddCycleGridStepRecoverySlot(slotIsBuy, slotLevel, slotPrice, slotCount,
                                   isBuy, level, OrderGetDouble(ORDER_PRICE_OPEN));
     }

   if(!pendingOnly)
     {
      for(int i = PositionsTotal() - 1; i >= 0; i--)
        {
         ulong ticket = PositionGetTicket(i);
         if(ticket == 0)
            continue;
         if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
            continue;
         if(PositionGetString(POSITION_SYMBOL) != g_sym)
            continue;
         if(IsFloatTicket(ticket))
            continue;                    // 0.0.44: a floated leg's old price must not poison the recovered grid step
         bool isBuy; int level;
         if(!ParseLegComment(PositionGetString(POSITION_COMMENT), isBuy, level))
            continue;
         AddCycleGridStepRecoverySlot(slotIsBuy, slotLevel, slotPrice, slotCount,
                                      isBuy, level, PositionGetDouble(POSITION_PRICE_OPEN));
        }
     }

   if(slotCount < 2)
      return 0.0;

   double candidates[];
   int candidateCount = 0;
   for(int i = 0; i < slotCount - 1; i++)
     {
      for(int j = i + 1; j < slotCount; j++)
        {
         double denominator = 0.0;
         if(slotIsBuy[i] == slotIsBuy[j])
            denominator = MathAbs(slotLevel[i] - slotLevel[j]);
         else
            denominator = slotLevel[i] + slotLevel[j];

         if(denominator <= 0.0)
            continue;
         AddCycleGridStepCandidate(candidates, candidateCount,
                                   MathAbs(slotPrice[i] - slotPrice[j]) / denominator);
        }
     }

   return SelectCycleGridStepCandidate(candidates, candidateCount);
  }

void ApplyRecoveredCycleGridStepDistance(const double step,
                                         const string source,
                                         const int positions,
                                         const int pendings)
  {
   g_cycleGridStepDistance = SanitizeCycleGridStepDistance(step);
   if(g_cycleGridStepDistance <= 0.0)
      return;

   PersistCycleGridStepDistance();
   Log(1, StringFormat("Straddle: cycle grid step snapshot recovered source=%s rawGridStep=%.2f snapshottedGridStep=%s liveScale=%.4f liveGridStep=%s positions=%d pendings=%d",
                       source,
                       InpGridStepUSD,
                       DoubleToString(g_cycleGridStepDistance, g_digits),
                       PriceScaleFactor(),
                       DoubleToString(GridStepDistance(), g_digits),
                       positions,
                       pendings));
  }

void EnsureCycleGridStepDistanceForRecovery(const int positions, const int pendings)
  {
   if(g_cycleGridStepDistance > 0.0)
      return;

   double inferred = InferCycleGridStepDistanceFromTickets(true);
   if(inferred > 0.0)
     {
      ApplyRecoveredCycleGridStepDistance(inferred, "pending_spacing", positions, pendings);
      return;
     }

   inferred = InferCycleGridStepDistanceFromTickets(false);
   if(inferred > 0.0)
     {
      ApplyRecoveredCycleGridStepDistance(inferred, "ticket_spacing", positions, pendings);
      return;
     }

   double fallback = LegacyCycleGridStepDistance();
   string source = "legacy_raw_grid_step";
   if(fallback <= 0.0)
     {
      fallback = GridStepDistance();
      source = "live_adaptive_fallback";
     }
   ApplyRecoveredCycleGridStepDistance(fallback, source, positions, pendings);
  }

double CycleGridStepDistance()
  {
   if(g_cycleGridStepDistance > 0.0)
      return g_cycleGridStepDistance;

   double fallback = LegacyCycleGridStepDistance();
   string source = "legacy_raw_grid_step";
   if(fallback <= 0.0)
     {
      fallback = GridStepDistance();
      source = "live_adaptive_fallback";
     }

   g_cycleGridStepDistance = SanitizeCycleGridStepDistance(fallback);
   Log(0, StringFormat("Straddle: WARN cycle grid step snapshot missing, using %s=%s liveScale=%.4f liveGridStep=%s",
                       source,
                       DoubleToString(g_cycleGridStepDistance, g_digits),
                       PriceScaleFactor(),
                       DoubleToString(GridStepDistance(), g_digits)));
   return g_cycleGridStepDistance;
  }

void SnapshotCycleGridStepDistance()
  {
   RefreshPriceScaleCacheIfNeeded(true);
   g_cycleGridStepDistance = SanitizeCycleGridStepDistance(GridStepDistance());
   PersistCycleGridStepDistance();
   Log(1, StringFormat("Straddle: cycle grid step snapshot rawGridStep=%.2f snapshottedGridStep=%s liveScale=%.4f",
                       InpGridStepUSD,
                       DoubleToString(g_cycleGridStepDistance, g_digits),
                       PriceScaleFactor()));
  }

//+------------------------------------------------------------------+
//| Minimum distance a pending stop must keep from market             |
//+------------------------------------------------------------------+
double MinStopDistance()
  {
   double point  = SymbolInfoDouble(g_sym, SYMBOL_POINT);
   long   stops  = SymbolInfoInteger(g_sym, SYMBOL_TRADE_STOPS_LEVEL);
   long   freeze = SymbolInfoInteger(g_sym, SYMBOL_TRADE_FREEZE_LEVEL);
   long   lvl    = (stops > freeze ? stops : freeze);
   return (double)lvl * point + TickSize();          // +1 tick safety margin
  }

//+------------------------------------------------------------------+
//| Refresh current tradable prices for send-time validation          |
//+------------------------------------------------------------------+
bool RefreshCurrentPrices(double &bid, double &ask)
  {
   if(IsStopped())
      return false;

   MqlTick tick;
   if(SymbolInfoTick(g_sym, tick))
     {
      bid = tick.bid;
      ask = tick.ask;
     }
   else
     {
      bid = 0.0;
      ask = 0.0;
     }

   if(bid <= 0.0 || ask <= 0.0)
     {
      if(!g_symbol.RefreshRates())
         return false;
      bid = g_symbol.Bid();
      ask = g_symbol.Ask();
     }

   return (bid > 0.0 && ask > 0.0);
  }

//+------------------------------------------------------------------+
//| Validate a fixed grid stop price immediately before sending       |
//+------------------------------------------------------------------+
bool ValidatePendingStopPriceForSend(const bool isBuy, const double price, double &bid, double &ask)
  {
   if(price <= 0.0)
      return false;
   if(!RefreshCurrentPrices(bid, ask))
      return false;

   double minDist = MinStopDistance();
   if(isBuy)
      return (price >= SnapUp(ask + minDist));
   return (price <= SnapDown(bid - minDist));
  }

//+------------------------------------------------------------------+
//| Clamp + round a volume to SYMBOL_VOLUME_MIN/MAX/STEP              |
//+------------------------------------------------------------------+
double NormalizeLot(double lots)
  {
   double vmin  = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   double vmax  = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MAX);
   double vstep = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP);
   if(vstep > 0.0)
      lots = MathRound(lots / vstep) * vstep;
   if(vmin > 0.0 && lots < vmin)
      lots = vmin;
   if(vmax > 0.0 && lots > vmax)
      lots = vmax;
   return NormalizeDouble(lots, 8);
  }

//+------------------------------------------------------------------+
//| Lot size for grid level k (1-based): near / mid / far tiers       |
//+------------------------------------------------------------------+
double TierLot(const int level)
  {
   const int nearCount = TierNearCount();
   const int midCount  = TierMidCount();
   double lot = InpLotFar;
   if(level <= nearCount)
      lot = (IsAutoLotMode() ? InpAutoLotNear : InpLotNear);
   else if(level <= nearCount + midCount)
      lot = (IsAutoLotMode() ? InpAutoLotMid : InpLotMid);
   else
      lot = (IsAutoLotMode() ? InpAutoLotFar : InpLotFar);
   return (IsAutoLotMode() ? lot * AutoLotScaleFactor() : lot);
  }

void MarketValidationResetZeroGridTracking()
  {
   g_marketValidationMinLotInflationSkip = false;
   g_marketValidationOtherZeroPlacementCause = false;
  }

void MarketValidationMarkMinLotInflationSkip()
  {
   g_marketValidationMinLotInflationSkip = true;
  }

void MarketValidationMarkOtherZeroPlacementCause()
  {
   g_marketValidationOtherZeroPlacementCause = true;
  }

bool MarketValidationGridLotSafe(const double requestedLots,
                                 const double normalizedLots,
                                 const int level)
  {
   if(!InpUseMarketValidationSafety)
      return true;
   if(requestedLots <= 0.0 || normalizedLots <= 0.0)
      return false;
   if(normalizedLots <= requestedLots + 1.0e-8)
      return true;

   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
   double smallAccountLimit = MoneyInput(InpMarketValidationSmallAccountEquityUSD);
   bool smallAccount = (smallAccountLimit > 0.0 &&
                        ((equity > 0.0 && equity <= smallAccountLimit) ||
                         (freeMargin > 0.0 && freeMargin <= smallAccountLimit)));
   if(!smallAccount)
      return true;

   double ratio = normalizedLots / MathMax(requestedLots, 1.0e-8);
   bool ratioTooHigh = (InpMarketValidationMaxLotInflationRatio > 0.0 &&
                        ratio > InpMarketValidationMaxLotInflationRatio + 1.0e-8);
   bool lotTooHigh=(g_normalizedMarketValidationInflatedLotCap>0.0 &&
                    normalizedLots>g_normalizedMarketValidationInflatedLotCap+1.0e-8);
   if(!ratioTooHigh && !lotTooHigh)
      return true;

   LogOp(StringFormat("Straddle: grid level skipped - broker min lot inflation unsafe on small account level=%d requested=%.4f normalized=%.4f ratio=%.2f equity=%.2f freeMargin=%.2f",
                      level, requestedLots, normalizedLots, ratio, equity, freeMargin));
   MarketValidationMarkMinLotInflationSkip();
   return false;
  }

// A fresh grid level is a BUY/SELL pair. Check the pair together before the
// first order is sent so a projected-margin rejection cannot leave only one
// side of that level on the chart.
bool MarketValidationPairMarginSafe(const int level,
                                    const double buyLots,
                                    const double sellLots)
  {
   if(buyLots <= 0.0 || sellLots <= 0.0)
      return false;

   const double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   const double currentMargin = AccountInfoDouble(ACCOUNT_MARGIN);
   const double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
   const double reserve = MoneyInput(InpMarketValidationMinFreeMarginAfterCheckUSD);
   const double marginFloor = MarketValidationMinMarginLevelFloorPct();
   const double buyPrice = LevelPrice(true, level);
   const double sellPrice = LevelPrice(false, level);
   double buyMargin = 0.0;
   double sellMargin = 0.0;

   if(!MathIsValidNumber(equity) || equity <= 0.0 ||
      !MathIsValidNumber(currentMargin) || currentMargin < 0.0 ||
      !OrderCalcMargin(ORDER_TYPE_BUY, g_sym, buyLots, buyPrice, buyMargin) ||
      !OrderCalcMargin(ORDER_TYPE_SELL, g_sym, sellLots, sellPrice, sellMargin) ||
      !MathIsValidNumber(buyMargin) || buyMargin < 0.0 ||
      !MathIsValidNumber(sellMargin) || sellMargin < 0.0)
      return false;

   const double projectedMargin = currentMargin + buyMargin + sellMargin;
   const double projectedFreeMargin = equity - projectedMargin;
   const double projectedMarginLevel = (projectedMargin > 0.0
                                        ? equity / projectedMargin * 100.0
                                        : 1.0e100);
   const bool safe = (MathIsValidNumber(projectedMargin) &&
                      MathIsValidNumber(projectedFreeMargin) &&
                      MathIsValidNumber(projectedMarginLevel) &&
                      projectedFreeMargin > reserve &&
                      (marginFloor <= 0.0 || projectedMarginLevel >= marginFloor));
   if(!safe)
      LogState(0, "pair-margin-abort",
               StringFormat("Straddle: margin cap before pair L%d BUY %.2f + SELL %.2f required=%.0f/%.0f projectedFree=%.0f currentFree=%.0f projectedMarginLevel=%.1f floor=%.1f reserve=%.0f",
                            level, buyLots, sellLots, buyMargin, sellMargin,
                            projectedFreeMargin, freeMargin, projectedMarginLevel,
                            marginFloor, reserve), 60);
   return safe;
  }

//+------------------------------------------------------------------+
//| Canonical request helpers                                         |
//+------------------------------------------------------------------+
double VolumeTolerance(const string symbol)
  {
   double step=0.0;
   if(!SymbolInfoDouble(symbol,SYMBOL_VOLUME_STEP,step) ||
      !MathIsValidNumber(step) || step<=0.0)
      return 1.0e-8;
   return MathMax(1.0e-8,step*0.5);
  }

double NormalizeVolumeDown(const string symbol,const double volume)
  {
   if(!MathIsValidNumber(volume) || volume<=0.0)
      return 0.0;
   double step=0.0;
   if(!SymbolInfoDouble(symbol,SYMBOL_VOLUME_STEP,step) ||
      !MathIsValidNumber(step) || step<=0.0)
      return 0.0;
   double normalized=NormalizeDouble(MathFloor(volume/step+1.0e-12)*step,8);
   while(normalized>volume+1.0e-10 && normalized>0.0)
      normalized=NormalizeDouble(normalized-step,8);
   return MathMax(0.0,normalized);
  }

void InitializeTradeIntent(StrTradeIntent &intent)
  {
   ZeroMemory(intent);
   intent.kind=STR_OP_INITIALIZE;
   intent.symbol=g_sym;
   intent.magic=InpMagic;
   intent.deviation=(uint)InpDeviationPoints;
   intent.margin_floor_pct=0.0;
   intent.free_margin_reserve=0.0;
  }

bool IsBuyOrderType(const ENUM_ORDER_TYPE type)
  {
   return (type==ORDER_TYPE_BUY || type==ORDER_TYPE_BUY_LIMIT ||
           type==ORDER_TYPE_BUY_STOP || type==ORDER_TYPE_BUY_STOP_LIMIT);
  }

bool IsSellOrderType(const ENUM_ORDER_TYPE type)
  {
   return (type==ORDER_TYPE_SELL || type==ORDER_TYPE_SELL_LIMIT ||
           type==ORDER_TYPE_SELL_STOP || type==ORDER_TYPE_SELL_STOP_LIMIT);
  }

bool ResolveMarginOrderType(const ENUM_ORDER_TYPE requestedType,
                            ENUM_ORDER_TYPE &marginOrderType)
  {
   switch(requestedType)
     {
      case ORDER_TYPE_BUY:
      case ORDER_TYPE_BUY_LIMIT:
      case ORDER_TYPE_BUY_STOP:
      case ORDER_TYPE_BUY_STOP_LIMIT:
         marginOrderType=ORDER_TYPE_BUY;
         return true;
      case ORDER_TYPE_SELL:
      case ORDER_TYPE_SELL_LIMIT:
      case ORDER_TYPE_SELL_STOP:
      case ORDER_TYPE_SELL_STOP_LIMIT:
         marginOrderType=ORDER_TYPE_SELL;
         return true;
      default:
         return false;
     }
  }

bool CaptureTradeSnapshot(const StrTradeIntent &intent,
                          StrTradeSnapshot &snapshot,
                          ENUM_STR_REASON_CODE &reason)
  {
   ZeroMemory(snapshot);
   reason=STR_REASON_NONE;
   if(StringLen(intent.symbol)<=0 || IsStopped())
     {
      reason=DEFER_MARKET_CLOSED;
      return false;
     }

   bool newExposure=(intent.kind==STR_OP_PENDING_PLACE || intent.kind==STR_OP_MARKET_DEAL);
   bool immediate=(intent.kind==STR_OP_MARKET_DEAL || intent.kind==STR_OP_POSITION_REDUCE);
   bool needsOrderMode=(intent.kind==STR_OP_PENDING_PLACE || intent.kind==STR_OP_MARKET_DEAL ||
                        intent.kind==STR_OP_SLTP || intent.kind==STR_OP_PENDING_MODIFY);
   bool needsExpiration=(intent.kind==STR_OP_PENDING_PLACE ||
                         (intent.kind==STR_OP_PENDING_MODIFY && intent.change_time_policy));
   bool needsGeometry=(intent.kind==STR_OP_PENDING_PLACE || intent.kind==STR_OP_MARKET_DEAL ||
                       intent.kind==STR_OP_SLTP || intent.kind==STR_OP_PENDING_MODIFY);
   bool needsPrices=(immediate || needsGeometry);
   bool needsPoint=(immediate || needsGeometry);
   long digits=0,stops=0,freeze=0;
   if(!SymbolInfoInteger(intent.symbol,SYMBOL_TRADE_MODE,snapshot.trade_mode))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(immediate && !SymbolInfoInteger(intent.symbol,SYMBOL_TRADE_EXEMODE,snapshot.execution_mode))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   // Filling masks are meaningful only to Market/Exchange immediate DEALs.
   // Pending, delete, SLTP and Instant/Request operations never consult them.
   if(immediate &&
      (snapshot.execution_mode==SYMBOL_TRADE_EXECUTION_MARKET ||
       snapshot.execution_mode==SYMBOL_TRADE_EXECUTION_EXCHANGE) &&
      !SymbolInfoInteger(intent.symbol,SYMBOL_FILLING_MODE,snapshot.filling_mode))
     {
      reason=REJECT_FILLING_UNSUPPORTED;
      return false;
     }
   if(needsOrderMode && !SymbolInfoInteger(intent.symbol,SYMBOL_ORDER_MODE,snapshot.order_mode))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(needsExpiration &&
      !SymbolInfoInteger(intent.symbol,SYMBOL_EXPIRATION_MODE,snapshot.expiration_mode))
     {
      reason=REJECT_EXPIRATION_UNSUPPORTED;
      return false;
     }
   if(needsGeometry &&
      (!SymbolInfoInteger(intent.symbol,SYMBOL_DIGITS,digits) ||
       !SymbolInfoInteger(intent.symbol,SYMBOL_TRADE_STOPS_LEVEL,stops) ||
       !SymbolInfoInteger(intent.symbol,SYMBOL_TRADE_FREEZE_LEVEL,freeze)))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(needsPrices &&
      (!SymbolInfoDouble(intent.symbol,SYMBOL_BID,snapshot.bid) ||
       !SymbolInfoDouble(intent.symbol,SYMBOL_ASK,snapshot.ask)))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(needsPoint && !SymbolInfoDouble(intent.symbol,SYMBOL_POINT,snapshot.point))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(newExposure &&
      (!SymbolInfoDouble(intent.symbol,SYMBOL_VOLUME_MIN,snapshot.volume_min) ||
       !SymbolInfoDouble(intent.symbol,SYMBOL_VOLUME_MAX,snapshot.volume_max) ||
       !SymbolInfoDouble(intent.symbol,SYMBOL_VOLUME_STEP,snapshot.volume_step) ||
       !SymbolInfoDouble(intent.symbol,SYMBOL_VOLUME_LIMIT,snapshot.volume_limit)))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }

   snapshot.account_trade_allowed=(AccountInfoInteger(ACCOUNT_TRADE_ALLOWED)!=0);
   snapshot.account_expert_allowed=(AccountInfoInteger(ACCOUNT_TRADE_EXPERT)!=0);
   snapshot.terminal_trade_allowed=(TerminalInfoInteger(TERMINAL_TRADE_ALLOWED)!=0);
   snapshot.mql_trade_allowed=(MQLInfoInteger(MQL_TRADE_ALLOWED)!=0);
   if(newExposure)
     {
      snapshot.equity=AccountInfoDouble(ACCOUNT_EQUITY);
      snapshot.current_margin=AccountInfoDouble(ACCOUNT_MARGIN);
      snapshot.current_free_margin=AccountInfoDouble(ACCOUNT_MARGIN_FREE);
      snapshot.stopout_call=AccountInfoDouble(ACCOUNT_MARGIN_SO_CALL);
      snapshot.stopout_stop=AccountInfoDouble(ACCOUNT_MARGIN_SO_SO);
      snapshot.stopout_mode=AccountInfoInteger(ACCOUNT_MARGIN_SO_MODE);
      snapshot.margin_mode=AccountInfoInteger(ACCOUNT_MARGIN_MODE);
      snapshot.hedging=(snapshot.margin_mode==ACCOUNT_MARGIN_MODE_RETAIL_HEDGING);
      snapshot.hedge_allowed=(AccountInfoInteger(ACCOUNT_HEDGE_ALLOWED)!=0);
     }
   if(intent.kind==STR_OP_PENDING_PLACE)
      snapshot.account_order_limit=AccountInfoInteger(ACCOUNT_LIMIT_ORDERS);
   if(intent.kind==STR_OP_POSITION_REDUCE)
      snapshot.fifo_close=(AccountInfoInteger(ACCOUNT_FIFO_CLOSE)!=0);
   snapshot.digits=(int)digits;
   snapshot.stops_level=(int)MathMax(0,stops);
   snapshot.freeze_level=(int)MathMax(0,freeze);
   if(intent.kind==STR_OP_PENDING_PLACE)
      snapshot.pending_count=OrdersTotal();
   if(newExposure)
     {
      bool directionalBuy=IsBuyOrderType(intent.order_type);
      snapshot.directional_volume=0.0;
      for(int i=PositionsTotal()-1;i>=0;i--)
        {
         ulong ticket=PositionGetTicket(i);
         if(ticket==0 || PositionGetString(POSITION_SYMBOL)!=intent.symbol ||
            (((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY)!=directionalBuy))
            continue;
         double volume=PositionGetDouble(POSITION_VOLUME);
         if(!MathIsValidNumber(volume) || volume<0.0)
           { reason=REJECT_SYMBOL_CAPABILITY; return false; }
         snapshot.directional_volume+=volume;
        }
      for(int i=OrdersTotal()-1;i>=0;i--)
        {
         ulong ticket=OrderGetTicket(i);
         if(ticket==0 || OrderGetString(ORDER_SYMBOL)!=intent.symbol ||
            (IsBuyOrderType((ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE))!=directionalBuy))
            continue;
         double volume=OrderGetDouble(ORDER_VOLUME_CURRENT);
         if(!MathIsValidNumber(volume) || volume<0.0)
           { reason=REJECT_SYMBOL_CAPABILITY; return false; }
         snapshot.directional_volume+=volume;
        }
     }
   snapshot.captured_at=TimeCurrent();
   g_tradeSnapshotGeneration++;
   if(g_tradeSnapshotGeneration==0)
      g_tradeSnapshotGeneration=1;
   snapshot.generation=g_tradeSnapshotGeneration;

   if(needsPrices &&
      (!MathIsValidNumber(snapshot.bid) || !MathIsValidNumber(snapshot.ask) ||
       snapshot.bid<=0.0 || snapshot.ask<=0.0 || snapshot.ask<snapshot.bid))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(needsPoint && (!MathIsValidNumber(snapshot.point) || snapshot.point<=0.0))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(newExposure &&
      (!MathIsValidNumber(snapshot.volume_min) || !MathIsValidNumber(snapshot.volume_max) ||
       !MathIsValidNumber(snapshot.volume_step) || !MathIsValidNumber(snapshot.volume_limit) ||
       !MathIsValidNumber(snapshot.directional_volume) ||
       !MathIsValidNumber(snapshot.equity) || !MathIsValidNumber(snapshot.current_margin) ||
       !MathIsValidNumber(snapshot.current_free_margin) ||
       !MathIsValidNumber(snapshot.stopout_call) || !MathIsValidNumber(snapshot.stopout_stop) ||
       snapshot.volume_min<=0.0 || snapshot.volume_max<snapshot.volume_min ||
       snapshot.volume_step<=0.0))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(!snapshot.account_trade_allowed || !snapshot.account_expert_allowed ||
      !snapshot.terminal_trade_allowed || !snapshot.mql_trade_allowed)
     {
      reason=REJECT_ACCOUNT_CAPABILITY;
      return false;
     }
   if(!InTradeSession())
     {
      reason=DEFER_MARKET_CLOSED;
      return false;
     }
   return true;
  }

bool ResolvePendingExpiration(const StrTradeSnapshot &snapshot,
                              ENUM_ORDER_TYPE_TIME &timeType,
                              datetime &expiration,
                              ENUM_STR_REASON_CODE &reason)
  {
   reason=STR_REASON_NONE;
   expiration=0;
   if((snapshot.expiration_mode & SYMBOL_EXPIRATION_GTC)!=0)
     {
      timeType=ORDER_TIME_GTC;
      return true;
     }
   if((snapshot.expiration_mode & SYMBOL_EXPIRATION_DAY)!=0)
     {
      timeType=ORDER_TIME_DAY;
      return true;
     }
   if((snapshot.expiration_mode & SYMBOL_EXPIRATION_SPECIFIED_DAY)!=0)
     {
      datetime serverNow=TimeTradeServer();
      MqlDateTime serverDate;
      if(serverNow<=0 || !TimeToStruct(serverNow,serverDate))
        {
         reason=REJECT_EXPIRATION_UNSUPPORTED;
         return false;
        }
      serverDate.hour=23;
      serverDate.min=59;
      serverDate.sec=59;
      expiration=StructToTime(serverDate);
      if(expiration<=serverNow)
        {
         reason=REJECT_EXPIRATION_UNSUPPORTED;
         return false;
        }
      timeType=ORDER_TIME_SPECIFIED_DAY;
      return true;
     }
   if((snapshot.expiration_mode & SYMBOL_EXPIRATION_SPECIFIED)!=0)
     {
      datetime serverNow=TimeTradeServer();
      if(serverNow<=0 || (long)serverNow>LONG_MAX-86400)
        {
         reason=REJECT_EXPIRATION_UNSUPPORTED;
         return false;
        }
      timeType=ORDER_TIME_SPECIFIED;
      expiration=serverNow+86400;
      return true;
     }
   reason=REJECT_EXPIRATION_UNSUPPORTED;
   return false;
  }

bool ResolveImmediateFilling(const StrTradeIntent &intent,
                             const StrTradeSnapshot &snapshot,
                             ENUM_ORDER_TYPE_FILLING &filling,
                             ENUM_STR_REASON_CODE &reason)
  {
   reason=STR_REASON_NONE;
   if(intent.kind==STR_OP_PENDING_PLACE || intent.kind==STR_OP_PENDING_MODIFY)
     {
      filling=ORDER_FILLING_RETURN;
      return true;
     }
   if(snapshot.execution_mode==SYMBOL_TRADE_EXECUTION_INSTANT ||
      snapshot.execution_mode==SYMBOL_TRADE_EXECUTION_REQUEST)
     {
      filling=ORDER_FILLING_FOK;
      return true;
     }
   if(snapshot.execution_mode==SYMBOL_TRADE_EXECUTION_MARKET ||
      snapshot.execution_mode==SYMBOL_TRADE_EXECUTION_EXCHANGE)
     {
      long mask=0;
      if(!SymbolInfoInteger(intent.symbol,SYMBOL_FILLING_MODE,mask) ||
         mask!=snapshot.filling_mode)
        {
         reason=REJECT_FILLING_UNSUPPORTED;
         return false;
        }
      if((mask & SYMBOL_FILLING_FOK)!=0)
        {
         filling=ORDER_FILLING_FOK;
         return true;
        }
      if((mask & SYMBOL_FILLING_IOC)!=0)
        {
         filling=ORDER_FILLING_IOC;
         return true;
        }
      if(snapshot.execution_mode==SYMBOL_TRADE_EXECUTION_EXCHANGE)
        {
         filling=ORDER_FILLING_RETURN;
         return true;
        }
      reason=REJECT_FILLING_UNSUPPORTED;
      return false;
     }
   reason=REJECT_FILLING_UNSUPPORTED;
   return false;
  }

bool BuildPendingStopRequest(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                             MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   ZeroMemory(request);
   ENUM_ORDER_TYPE_TIME timeType=ORDER_TIME_GTC;
   datetime expiration=0;
   if(!ResolvePendingExpiration(snapshot,timeType,expiration,reason))
      return false;
   request.action=TRADE_ACTION_PENDING;
   request.magic=(ulong)intent.magic;
   request.symbol=intent.symbol;
   request.volume=intent.requested_volume;
   request.type=intent.order_type;
   request.price=intent.fixed_price;
   request.stoplimit=intent.stoplimit;
   request.sl=intent.sl;
   request.tp=intent.tp;
   request.deviation=(ulong)intent.deviation;
   request.type_filling=ORDER_FILLING_RETURN;
   request.type_time=timeType;
   request.expiration=expiration;
   request.comment=intent.comment;
   request.position=intent.position;
   request.position_by=intent.position_by;
   return true;
  }

bool BuildMarketDealRequest(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                            MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   ZeroMemory(request);
   ENUM_ORDER_TYPE_FILLING filling=ORDER_FILLING_FOK;
   if(!ResolveImmediateFilling(intent,snapshot,filling,reason))
      return false;
   request.action=TRADE_ACTION_DEAL;
   request.magic=(ulong)intent.magic;
   request.symbol=intent.symbol;
   request.volume=intent.requested_volume;
   request.type=intent.order_type;
   request.price=(IsBuyOrderType(intent.order_type) ? snapshot.ask : snapshot.bid);
   request.stoplimit=intent.stoplimit;
   request.sl=intent.sl;
   request.tp=intent.tp;
   request.deviation=(ulong)intent.deviation;
   request.type_filling=filling;
   request.type_time=ORDER_TIME_GTC;
   request.expiration=0;
   request.comment=intent.comment;
   request.position=intent.position;
   request.position_by=intent.position_by;
   return true;
  }

bool BuildProtectionRequest(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                            MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   ZeroMemory(request);
   reason=STR_REASON_NONE;
   request.action=TRADE_ACTION_SLTP;
   request.magic=(ulong)intent.magic;
   request.symbol=intent.symbol;
   request.position=intent.position;
   request.sl=intent.sl;
   request.tp=intent.tp;
   request.comment=intent.comment;
   return true;
  }

bool IsPendingTradeOrderType(const ENUM_ORDER_TYPE type)
  {
   return (type==ORDER_TYPE_BUY_LIMIT || type==ORDER_TYPE_SELL_LIMIT ||
           type==ORDER_TYPE_BUY_STOP || type==ORDER_TYPE_SELL_STOP ||
           type==ORDER_TYPE_BUY_STOP_LIMIT || type==ORDER_TYPE_SELL_STOP_LIMIT);
  }

bool PendingTimePolicySupported(const StrTradeSnapshot &snapshot,
                                const ENUM_ORDER_TYPE_TIME timeType,
                                const datetime expiration,
                                ENUM_STR_REASON_CODE &reason)
  {
   reason=STR_REASON_NONE;
   if(timeType==ORDER_TIME_GTC || timeType==ORDER_TIME_DAY)
     {
      long capability=(timeType==ORDER_TIME_GTC ? SYMBOL_EXPIRATION_GTC : SYMBOL_EXPIRATION_DAY);
      if((snapshot.expiration_mode & capability)!=0 && expiration==0)
         return true;
      reason=REJECT_EXPIRATION_UNSUPPORTED;
      return false;
     }
   long capability=(timeType==ORDER_TIME_SPECIFIED_DAY ? SYMBOL_EXPIRATION_SPECIFIED_DAY :
                    (timeType==ORDER_TIME_SPECIFIED ? SYMBOL_EXPIRATION_SPECIFIED : 0));
   datetime serverNow=TimeTradeServer();
   if(capability!=0 && (snapshot.expiration_mode & capability)!=0 &&
      serverNow>0 && expiration>serverNow)
      return true;
   reason=REJECT_EXPIRATION_UNSUPPORTED;
   return false;
  }

// Builds a complete immutable pending-modification intent from the live order.
// No strategy calls this factory yet; callers must opt into every changed field.
bool CreatePendingModifyIntent(const ulong ticket,
                               const bool changePrice,const double requestedPrice,
                               const bool changeStoplimit,const double requestedStoplimit,
                               const bool changeSL,const double requestedSL,
                               const bool changeTP,const double requestedTP,
                               const bool changeTimePolicy,
                               const ENUM_ORDER_TYPE_TIME requestedTimeType,
                               const datetime requestedExpiration,
                               const string comment,
                               StrTradeIntent &intent,
                               ENUM_STR_REASON_CODE &reason)
  {
   InitializeTradeIntent(intent);
   reason=STR_REASON_NONE;
   if(ticket==0 || !OrderSelect(ticket) ||
      (ulong)OrderGetInteger(ORDER_TICKET)!=ticket ||
      OrderGetInteger(ORDER_MAGIC)!=intent.magic ||
      OrderGetString(ORDER_SYMBOL)!=intent.symbol)
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   ENUM_ORDER_TYPE type=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
   if(!IsPendingTradeOrderType(type))
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }

   intent.kind=STR_OP_PENDING_MODIFY;
   intent.order=ticket;
   intent.order_identifier=(ulong)OrderGetInteger(ORDER_TICKET);
   intent.order_setup_time_msc=OrderGetInteger(ORDER_TIME_SETUP_MSC);
   intent.order_type=type;
   intent.original_time_type=(ENUM_ORDER_TYPE_TIME)OrderGetInteger(ORDER_TYPE_TIME);
   intent.original_expiration=(datetime)OrderGetInteger(ORDER_TIME_EXPIRATION);
   intent.original_price=OrderGetDouble(ORDER_PRICE_OPEN);
   intent.original_stoplimit=OrderGetDouble(ORDER_PRICE_STOPLIMIT);
   intent.original_sl=OrderGetDouble(ORDER_SL);
   intent.original_tp=OrderGetDouble(ORDER_TP);
   if(!MathIsValidNumber(intent.original_price) || intent.original_price<=0.0 ||
      !MathIsValidNumber(intent.original_stoplimit) || intent.original_stoplimit<0.0 ||
      !MathIsValidNumber(intent.original_sl) || intent.original_sl<0.0 ||
      !MathIsValidNumber(intent.original_tp) || intent.original_tp<0.0)
     {
      reason=REJECT_INVALID_INPUT;
      return false;
     }

   intent.fixed_price=intent.original_price;
   intent.stoplimit=intent.original_stoplimit;
   intent.sl=intent.original_sl;
   intent.tp=intent.original_tp;
   intent.requested_time_type=intent.original_time_type;
   intent.requested_expiration=intent.original_expiration;
   intent.change_price=changePrice;
   intent.change_stoplimit=changeStoplimit;
   intent.change_sl=changeSL;
   intent.change_tp=changeTP;
   intent.change_time_policy=changeTimePolicy;
   if(changePrice)
      intent.fixed_price=requestedPrice;
   if(changeStoplimit)
      intent.stoplimit=requestedStoplimit;
   if(changeSL)
      intent.sl=requestedSL;
   if(changeTP)
      intent.tp=requestedTP;
   if(changeTimePolicy)
     {
      intent.requested_time_type=requestedTimeType;
      intent.requested_expiration=requestedExpiration;
     }
   intent.comment=comment;
   intent.new_exposure=false;
   if(!MathIsValidNumber(intent.fixed_price) || intent.fixed_price<=0.0 ||
      !MathIsValidNumber(intent.stoplimit) || intent.stoplimit<0.0 ||
      !MathIsValidNumber(intent.sl) || intent.sl<0.0 ||
      !MathIsValidNumber(intent.tp) || intent.tp<0.0)
     {
      reason=REJECT_INVALID_INPUT;
      return false;
     }
   return true;
  }

bool BuildPendingModifyRequest(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                               MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   ZeroMemory(request);
   ENUM_ORDER_TYPE_TIME timeType=intent.original_time_type;
   datetime expiration=intent.original_expiration;
   if(intent.change_time_policy)
     {
      timeType=intent.requested_time_type;
      expiration=intent.requested_expiration;
      if(!PendingTimePolicySupported(snapshot,timeType,expiration,reason))
         return false;
     }
   else
      reason=STR_REASON_NONE;
   request.action=TRADE_ACTION_MODIFY;
   request.magic=(ulong)intent.magic;
   request.order=intent.order;
   request.symbol=intent.symbol;
   request.type=intent.order_type;
   request.price=(intent.change_price ? intent.fixed_price : intent.original_price);
   request.stoplimit=(intent.change_stoplimit ? intent.stoplimit : intent.original_stoplimit);
   request.sl=(intent.change_sl ? intent.sl : intent.original_sl);
   request.tp=(intent.change_tp ? intent.tp : intent.original_tp);
   request.type_filling=ORDER_FILLING_RETURN;
   request.type_time=timeType;
   request.expiration=expiration;
   request.comment=intent.comment;
   return true;
  }

bool BuildPositionReductionRequest(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                                   MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   ZeroMemory(request);
   ENUM_ORDER_TYPE_FILLING filling=ORDER_FILLING_FOK;
   if(!ResolveImmediateFilling(intent,snapshot,filling,reason))
      return false;
   request.action=TRADE_ACTION_DEAL;
   request.magic=(ulong)intent.magic;
   request.symbol=intent.symbol;
   request.volume=intent.requested_volume;
   request.type=intent.order_type;
   request.price=(IsBuyOrderType(intent.order_type) ? snapshot.ask : snapshot.bid);
   request.deviation=(ulong)intent.deviation;
   request.type_filling=filling;
   request.type_time=ORDER_TIME_GTC;
   request.comment=intent.comment;
   request.position=intent.position;
   request.position_by=intent.position_by;
   return true;
  }

bool BuildPendingDeleteRequest(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                               MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   ZeroMemory(request);
   reason=STR_REASON_NONE;
   request.action=TRADE_ACTION_REMOVE;
   request.magic=(ulong)intent.magic;
   request.order=intent.order;
   request.symbol=intent.symbol;
   request.comment=intent.comment;
   return true;
  }

bool TradeModeAllowsDirection(const long tradeMode,const ENUM_ORDER_TYPE type)
  {
   if(tradeMode==SYMBOL_TRADE_MODE_FULL)
      return true;
   if(tradeMode==SYMBOL_TRADE_MODE_LONGONLY)
      return IsBuyOrderType(type);
   if(tradeMode==SYMBOL_TRADE_MODE_SHORTONLY)
      return IsSellOrderType(type);
   return false;
  }

bool VolumeIsBrokerValid(const StrTradeSnapshot &snapshot,const MqlTradeRequest &request)
  {
   if(!MathIsValidNumber(request.volume) || request.volume<=0.0 ||
      request.volume+VolumeTolerance(request.symbol)<snapshot.volume_min ||
      request.volume>snapshot.volume_max+VolumeTolerance(request.symbol))
      return false;
   double down=NormalizeVolumeDown(request.symbol,request.volume);
   return (down>0.0 && MathAbs(down-request.volume)<=VolumeTolerance(request.symbol));
  }

bool ProjectedDirectionalVolumeOk(const StrTradeIntent &intent,
                                  const StrTradeSnapshot &snapshot,
                                  const MqlTradeRequest &request)
  {
   if(snapshot.volume_limit<=0.0)
      return true;
   return (MathIsValidNumber(snapshot.directional_volume) && snapshot.directional_volume>=0.0 &&
           snapshot.directional_volume+request.volume<=
           snapshot.volume_limit+VolumeTolerance(intent.symbol));
  }

bool CanOpenNewExposure(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                        const MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   reason=STR_REASON_NONE;
   if(!intent.new_exposure || !snapshot.account_trade_allowed ||
      !snapshot.account_expert_allowed || !snapshot.terminal_trade_allowed ||
      !snapshot.mql_trade_allowed || !snapshot.hedging || !snapshot.hedge_allowed ||
      !TradeModeAllowsDirection(snapshot.trade_mode,request.type) ||
      !VolumeIsBrokerValid(snapshot,request))
     {
      reason=REJECT_ACCOUNT_CAPABILITY;
      return false;
     }
   if(!ProjectedDirectionalVolumeOk(intent,snapshot,request))
     {
      reason=REJECT_EXPOSURE_CAP;
      return false;
     }

   switch(intent.kind)
     {
      case STR_OP_PENDING_PLACE:
        {
         bool pending=(request.action==TRADE_ACTION_PENDING);
         bool expiration=(request.type_time==ORDER_TIME_GTC || request.type_time==ORDER_TIME_DAY ||
                          request.type_time==ORDER_TIME_SPECIFIED || request.type_time==ORDER_TIME_SPECIFIED_DAY);
         if(!pending || !expiration ||
             (snapshot.order_mode & SYMBOL_ORDER_STOP)==0 ||
             (request.type!=ORDER_TYPE_BUY_STOP && request.type!=ORDER_TYPE_SELL_STOP))
           {
            reason=REJECT_SYMBOL_CAPABILITY;
            return false;
           }
         if(snapshot.account_order_limit>0 && snapshot.pending_count>=snapshot.account_order_limit)
           {
            reason=REJECT_EXPOSURE_CAP;
            return false;
           }
         ENUM_STR_REASON_CODE expirationReason=STR_REASON_NONE;
         if(!PendingTimePolicySupported(snapshot,request.type_time,request.expiration,expirationReason))
           {
            reason=expirationReason;
            return false;
           }
         if(request.sl!=0.0 && (snapshot.order_mode & SYMBOL_ORDER_SL)==0)
           {
            reason=REJECT_SYMBOL_CAPABILITY;
            return false;
           }
         if(request.tp!=0.0 && (snapshot.order_mode & SYMBOL_ORDER_TP)==0)
           {
            reason=REJECT_SYMBOL_CAPABILITY;
            return false;
           }
         double pendingDistance=(double)MathMax(snapshot.stops_level,snapshot.freeze_level)*snapshot.point;
         if((request.type==ORDER_TYPE_BUY_STOP && request.price<snapshot.ask+pendingDistance) ||
            (request.type==ORDER_TYPE_SELL_STOP && request.price>snapshot.bid-pendingDistance))
           {
            reason=DEFER_PRICE_CHANGED;
            return false;
           }
         bool buyPending=IsBuyOrderType(request.type);
         if((request.sl!=0.0 &&
             (buyPending ? request.sl>request.price-pendingDistance :
                           request.sl<request.price+pendingDistance)) ||
            (request.tp!=0.0 &&
             (buyPending ? request.tp<request.price+pendingDistance :
                           request.tp>request.price-pendingDistance)))
           {
            reason=REJECT_SYMBOL_CAPABILITY;
            return false;
           }
         break;
        }
      case STR_OP_MARKET_DEAL:
        {
         if(request.action!=TRADE_ACTION_DEAL ||
            (snapshot.order_mode & SYMBOL_ORDER_MARKET)==0 ||
            (request.type!=ORDER_TYPE_BUY && request.type!=ORDER_TYPE_SELL))
           {
            reason=REJECT_SYMBOL_CAPABILITY;
            return false;
           }
         ENUM_ORDER_TYPE_FILLING expected=request.type_filling;
         ENUM_STR_REASON_CODE fillingReason=STR_REASON_NONE;
         if(!ResolveImmediateFilling(intent,snapshot,expected,fillingReason) ||
            expected!=request.type_filling)
           {
            reason=REJECT_FILLING_UNSUPPORTED;
            return false;
           }
         if(request.sl!=0.0 && (snapshot.order_mode & SYMBOL_ORDER_SL)==0)
           {
            reason=REJECT_SYMBOL_CAPABILITY;
            return false;
           }
         if(request.tp!=0.0 && (snapshot.order_mode & SYMBOL_ORDER_TP)==0)
           {
            reason=REJECT_SYMBOL_CAPABILITY;
            return false;
           }
         double marketTolerance=MathMax(snapshot.point*0.5,1.0e-10);
         double marketPrice=(IsBuyOrderType(request.type) ? snapshot.ask : snapshot.bid);
         if(MathAbs(request.price-marketPrice)>marketTolerance)
           {
            reason=DEFER_PRICE_CHANGED;
            return false;
           }
         double marketDistance=(double)MathMax(snapshot.stops_level,snapshot.freeze_level)*snapshot.point;
         bool buyDeal=IsBuyOrderType(request.type);
         double protectionPrice=(buyDeal ? snapshot.bid : snapshot.ask);
         if((request.sl!=0.0 &&
             (buyDeal ? request.sl>protectionPrice-marketDistance :
                        request.sl<protectionPrice+marketDistance)) ||
            (request.tp!=0.0 &&
             (buyDeal ? request.tp<protectionPrice+marketDistance :
                        request.tp>protectionPrice-marketDistance)))
           {
            reason=REJECT_SYMBOL_CAPABILITY;
            return false;
           }
         break;
        }
      default:
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
     }

   ENUM_ORDER_TYPE marginOrderType=ORDER_TYPE_BUY;
   double required_margin=0.0;
   if(!ResolveMarginOrderType(request.type,marginOrderType) ||
      !OrderCalcMargin(marginOrderType,request.symbol,request.volume,request.price,required_margin) ||
      !MathIsValidNumber(required_margin) || required_margin<0.0 ||
      snapshot.current_free_margin<=required_margin+intent.free_margin_reserve)
     {
      reason=REJECT_MARGIN_PROJECTION;
      return false;
     }
   return true;
  }

bool CanModifyProtection(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                         const MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   reason=STR_REASON_NONE;
   if(!snapshot.account_trade_allowed || !snapshot.account_expert_allowed ||
      !snapshot.terminal_trade_allowed || !snapshot.mql_trade_allowed ||
      snapshot.trade_mode==SYMBOL_TRADE_MODE_DISABLED)
     {
      reason=REJECT_ACCOUNT_CAPABILITY;
      return false;
     }
   if(intent.kind==STR_OP_PENDING_MODIFY)
     {
      if(!OrderSelect(intent.order) ||
         (ulong)OrderGetInteger(ORDER_TICKET)!=intent.order_identifier ||
         OrderGetInteger(ORDER_TIME_SETUP_MSC)!=intent.order_setup_time_msc ||
         OrderGetInteger(ORDER_MAGIC)!=intent.magic ||
         OrderGetString(ORDER_SYMBOL)!=intent.symbol)
        {
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
        }
      if(IsPendingWithinFreeze(intent.order))
        {
         reason=DEFER_PRICE_CHANGED;
         return false;
        }
      ENUM_ORDER_TYPE originalType=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
      double tolerance=MathMax(snapshot.point*0.5,1.0e-10);
      if(OrderGetInteger(ORDER_TYPE_TIME)!=(long)intent.original_time_type ||
         OrderGetInteger(ORDER_TIME_EXPIRATION)!=(long)intent.original_expiration ||
         MathAbs(OrderGetDouble(ORDER_PRICE_OPEN)-intent.original_price)>tolerance ||
         MathAbs(OrderGetDouble(ORDER_PRICE_STOPLIMIT)-intent.original_stoplimit)>tolerance ||
         MathAbs(OrderGetDouble(ORDER_SL)-intent.original_sl)>tolerance ||
         MathAbs(OrderGetDouble(ORDER_TP)-intent.original_tp)>tolerance)
        {
         reason=DEFER_PRICE_CHANGED;
         return false;
        }
      bool originalTypeAllowed=((originalType==ORDER_TYPE_BUY_STOP || originalType==ORDER_TYPE_SELL_STOP)
                                ? ((snapshot.order_mode & SYMBOL_ORDER_STOP)!=0)
                                : ((originalType==ORDER_TYPE_BUY_LIMIT || originalType==ORDER_TYPE_SELL_LIMIT)
                                   ? ((snapshot.order_mode & SYMBOL_ORDER_LIMIT)!=0)
                                   : ((originalType==ORDER_TYPE_BUY_STOP_LIMIT || originalType==ORDER_TYPE_SELL_STOP_LIMIT) &&
                                      (snapshot.order_mode & SYMBOL_ORDER_STOP_LIMIT)!=0)));
      if(!originalTypeAllowed || originalType!=intent.order_type)
        {
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
        }
      double expectedPrice=(intent.change_price ? intent.fixed_price : intent.original_price);
      double expectedStoplimit=(intent.change_stoplimit ? intent.stoplimit : intent.original_stoplimit);
      double expectedSL=(intent.change_sl ? intent.sl : intent.original_sl);
      double expectedTP=(intent.change_tp ? intent.tp : intent.original_tp);
      ENUM_ORDER_TYPE_TIME expectedTime=(intent.change_time_policy ?
                                         intent.requested_time_type : intent.original_time_type);
      datetime expectedExpiration=(intent.change_time_policy ?
                                   intent.requested_expiration : intent.original_expiration);
      if(MathAbs(request.price-expectedPrice)>tolerance ||
         MathAbs(request.stoplimit-expectedStoplimit)>tolerance ||
         MathAbs(request.sl-expectedSL)>tolerance || MathAbs(request.tp-expectedTP)>tolerance ||
         request.type_time!=expectedTime || request.expiration!=expectedExpiration)
        {
         reason=REJECT_INVALID_INPUT;
         return false;
        }
      bool sl_changed=(intent.change_sl && MathAbs(intent.original_sl-request.sl)>tolerance);
      bool tp_changed=(intent.change_tp && MathAbs(intent.original_tp-request.tp)>tolerance);
      bool time_changed=(intent.change_time_policy &&
                         (intent.original_time_type!=request.type_time ||
                          intent.original_expiration!=request.expiration));
      bool price_changed=((intent.change_price &&
                           MathAbs(intent.original_price-request.price)>tolerance) ||
                          (intent.change_stoplimit &&
                           MathAbs(intent.original_stoplimit-request.stoplimit)>tolerance));
      bool changed=(price_changed || sl_changed || tp_changed || time_changed);
      if(!changed)
         return true;
      if(time_changed)
        {
         ENUM_STR_REASON_CODE timeReason=STR_REASON_NONE;
         if(!PendingTimePolicySupported(snapshot,request.type_time,request.expiration,timeReason))
           {
            reason=timeReason;
            return false;
           }
        }
      if(sl_changed && (snapshot.order_mode & SYMBOL_ORDER_SL)==0)
        {
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
        }
      if(tp_changed && (snapshot.order_mode & SYMBOL_ORDER_TP)==0)
        {
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
        }
      double minDistance=(double)MathMax(snapshot.stops_level,snapshot.freeze_level)*snapshot.point;
      if(price_changed)
        {
         if((originalType==ORDER_TYPE_BUY_STOP && request.price<snapshot.ask+minDistance) ||
            (originalType==ORDER_TYPE_SELL_STOP && request.price>snapshot.bid-minDistance) ||
            (originalType==ORDER_TYPE_BUY_LIMIT && request.price>snapshot.ask-minDistance) ||
            (originalType==ORDER_TYPE_SELL_LIMIT && request.price<snapshot.bid+minDistance) ||
            (originalType==ORDER_TYPE_BUY_STOP_LIMIT &&
             (request.price<snapshot.ask+minDistance || request.stoplimit>request.price)) ||
            (originalType==ORDER_TYPE_SELL_STOP_LIMIT &&
             (request.price>snapshot.bid-minDistance || request.stoplimit<request.price)))
           { reason=REJECT_SYMBOL_CAPABILITY; return false; }
        }
      bool buyPending=IsBuyOrderType(originalType);
      if(sl_changed && request.sl!=0.0 &&
         (buyPending ? request.sl>request.price-minDistance : request.sl<request.price+minDistance))
        { reason=REJECT_SYMBOL_CAPABILITY; return false; }
      if(tp_changed && request.tp!=0.0 &&
         (buyPending ? request.tp<request.price+minDistance : request.tp>request.price-minDistance))
        { reason=REJECT_SYMBOL_CAPABILITY; return false; }
      return true;
     }

   if(intent.kind!=STR_OP_SLTP || !PositionSelectByTicket(intent.position) ||
      PositionGetInteger(POSITION_MAGIC)!=intent.magic ||
      PositionGetString(POSITION_SYMBOL)!=intent.symbol ||
      (ulong)PositionGetInteger(POSITION_IDENTIFIER)!=intent.position_identifier)
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   double oldSL=PositionGetDouble(POSITION_SL);
   double oldTP=PositionGetDouble(POSITION_TP);
   bool sl_changed=(MathAbs(oldSL-request.sl)>snapshot.point*0.5);
   bool tp_changed=(MathAbs(oldTP-request.tp)>snapshot.point*0.5);
   bool changed=(sl_changed || tp_changed);
   if(!changed)
      return true;
   if(sl_changed && (snapshot.order_mode & SYMBOL_ORDER_SL)==0)
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }
   if(tp_changed && (snapshot.order_mode & SYMBOL_ORDER_TP)==0)
     {
      reason=REJECT_SYMBOL_CAPABILITY;
      return false;
     }

   double minDistance=(double)MathMax(snapshot.stops_level,snapshot.freeze_level)*snapshot.point;
   ENUM_POSITION_TYPE positionType=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   if(positionType==POSITION_TYPE_BUY)
     {
      if(sl_changed && request.sl!=0.0 && request.sl>snapshot.bid-minDistance)
        { reason=REJECT_SYMBOL_CAPABILITY; return false; }
      if(tp_changed && request.tp!=0.0 && request.tp<snapshot.bid+minDistance)
        { reason=REJECT_SYMBOL_CAPABILITY; return false; }
     }
   else
     {
      if(sl_changed && request.sl!=0.0 && request.sl<snapshot.ask+minDistance)
        { reason=REJECT_SYMBOL_CAPABILITY; return false; }
      if(tp_changed && request.tp!=0.0 && request.tp>snapshot.ask-minDistance)
        { reason=REJECT_SYMBOL_CAPABILITY; return false; }
     }
   return true;
  }

// MetaQuotes validation can expose a non-zero freeze level on FX symbols.
// A market reduction must be deferred when the position or its protected
// levels are inside that band; sending it anyway produces the validator error
// "Modification failed due to order or position being close to market".
bool PositionCloseInsideFreeze(const ulong ticket)
  {
   if(!PositionSelectByTicket(ticket))
      return false;

   const long freezeLevel=SymbolInfoInteger(g_sym,SYMBOL_TRADE_FREEZE_LEVEL);
   if(freezeLevel<=0)
      return false;

   const double point=SymbolInfoDouble(g_sym,SYMBOL_POINT);
   const double tick=TickSize();
   if(!MathIsValidNumber(point) || point<=0.0 ||
      !MathIsValidNumber(tick) || tick<=0.0)
      return true;

   double bid=0.0,ask=0.0;
   if(!RefreshCurrentPrices(bid,ask) || bid<=0.0 || ask<=0.0)
      return true;

   const ENUM_POSITION_TYPE positionType=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   const double closePrice=(positionType==POSITION_TYPE_BUY ? bid : ask);
   const double spread=MathMax(0.0,ask-bid);
   const double freezeDistance=(double)freezeLevel*point+spread+tick;
   const double openPrice=PositionGetDouble(POSITION_PRICE_OPEN);
   bool blocked=(openPrice>0.0 && MathAbs(closePrice-openPrice)<=freezeDistance);

   const double sl=PositionGetDouble(POSITION_SL);
   const double tp=PositionGetDouble(POSITION_TP);
   if(positionType==POSITION_TYPE_BUY)
     {
      if(sl>0.0 && closePrice-sl<=freezeDistance)
         blocked=true;
      if(tp>0.0 && tp-closePrice<=freezeDistance)
         blocked=true;
     }
   else
     {
      if(sl>0.0 && sl-closePrice<=freezeDistance)
         blocked=true;
      if(tp>0.0 && closePrice-tp<=freezeDistance)
         blocked=true;
     }

   if(blocked)
      LogState(1,"close-freeze",
               StringFormat("Straddle: close deferred for position #%I64u - freeze band active type=%s open=%s close=%s freezeDistance=%s sl=%s tp=%s",
                            ticket,
                            (positionType==POSITION_TYPE_BUY ? "BUY" : "SELL"),
                            DoubleToString(openPrice,g_digits),
                            DoubleToString(closePrice,g_digits),
                            DoubleToString(freezeDistance,g_digits),
                            DoubleToString(sl,g_digits),
                            DoubleToString(tp,g_digits)),
               30);
   return blocked;
  }

bool CanReduceOrDeleteRisk(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                           const MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   reason=STR_REASON_NONE;
   bool closeOnly=(snapshot.trade_mode==SYMBOL_TRADE_MODE_CLOSEONLY);
   if(!snapshot.account_trade_allowed || !snapshot.account_expert_allowed ||
      !snapshot.terminal_trade_allowed || !snapshot.mql_trade_allowed ||
      snapshot.trade_mode==SYMBOL_TRADE_MODE_DISABLED)
     {
      reason=REJECT_ACCOUNT_CAPABILITY;
      return false;
     }
   if(intent.kind==STR_OP_POSITION_REDUCE)
     {
      bool owned=PositionSelectByTicket(intent.position);
      if(!owned || PositionGetInteger(POSITION_MAGIC)!=intent.magic ||
         PositionGetString(POSITION_SYMBOL)!=intent.symbol ||
         (ulong)PositionGetInteger(POSITION_IDENTIFIER)!=intent.position_identifier ||
         request.volume<=0.0 || request.volume>PositionGetDouble(POSITION_VOLUME)+VolumeTolerance(intent.symbol))
        {
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
        }
      ENUM_POSITION_TYPE selectedType=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      ENUM_ORDER_TYPE expectedCloseType=(selectedType==POSITION_TYPE_BUY ? ORDER_TYPE_SELL : ORDER_TYPE_BUY);
      if(request.action!=TRADE_ACTION_DEAL || request.type!=expectedCloseType)
        {
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
        }
      if(PositionCloseInsideFreeze(intent.position))
        {
         reason=DEFER_PRICE_CHANGED;
         return false;
        }
      ENUM_ORDER_TYPE_FILLING expectedFilling=request.type_filling;
      ENUM_STR_REASON_CODE fillingReason=STR_REASON_NONE;
      if(!ResolveImmediateFilling(intent,snapshot,expectedFilling,fillingReason) ||
         expectedFilling!=request.type_filling)
        {
         reason=REJECT_FILLING_UNSUPPORTED;
         return false;
        }
      double livePrice=(IsBuyOrderType(request.type) ? snapshot.ask : snapshot.bid);
      if(MathAbs(request.price-livePrice)>MathMax(snapshot.point*0.5,1.0e-10))
        {
         reason=DEFER_PRICE_CHANGED;
         return false;
        }
      if(snapshot.fifo_close)
        {
         long selectedTime=PositionGetInteger(POSITION_TIME_MSC);
         for(int i=PositionsTotal()-1;i>=0;i--)
           {
            ulong other=PositionGetTicket(i);
            if(other==0 || other==intent.position ||
               PositionGetString(POSITION_SYMBOL)!=intent.symbol ||
               (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE)!=selectedType)
               continue;
            long otherTime=PositionGetInteger(POSITION_TIME_MSC);
            if(otherTime<selectedTime || (otherTime==selectedTime && other<intent.position))
              {
               reason=REJECT_ACCOUNT_CAPABILITY;
               return false;
              }
           }
        }
      if(!closeOnly && snapshot.trade_mode!=SYMBOL_TRADE_MODE_FULL &&
         snapshot.trade_mode!=SYMBOL_TRADE_MODE_LONGONLY &&
         snapshot.trade_mode!=SYMBOL_TRADE_MODE_SHORTONLY)
        {
         reason=REJECT_ACCOUNT_CAPABILITY;
         return false;
        }
      return true;
     }
   if(intent.kind==STR_OP_PENDING_DELETE)
     {
      bool owned=OrderSelect(intent.order);
      bool deletable=(owned && !IsPendingWithinFreeze(intent.order));
      if(!owned || !deletable || OrderGetInteger(ORDER_MAGIC)!=intent.magic ||
         OrderGetString(ORDER_SYMBOL)!=intent.symbol)
        {
         reason=DEFER_PENDING_DELETE;
         return false;
        }
      return true;
     }
   reason=REJECT_SYMBOL_CAPABILITY;
   return false;
  }

bool FinancialProjectionAllZero(const MqlTradeCheckResult &check)
  {
   return (check.balance==0.0 && check.equity==0.0 && check.profit==0.0 &&
           check.margin==0.0 && check.margin_free==0.0 && check.margin_level==0.0);
  }

bool ValidateProjectedMarginAndStopout(const StrTradeIntent &intent,
                                       const StrTradeSnapshot &snapshot,
                                       const MqlTradeRequest &request,
                                       const MqlTradeCheckResult &check,
                                       ENUM_STR_REASON_CODE &reason)
  {
   reason=STR_REASON_NONE;
   if(!intent.new_exposure)
      return true;

   double margin_floor=MathMax(0.0,intent.margin_floor_pct);
   double reserve=MathMax(0.0,intent.free_margin_reserve);
   ENUM_ORDER_TYPE marginOrderType=ORDER_TYPE_BUY;
   double requiredMargin=0.0;
   if(!ResolveMarginOrderType(request.type,marginOrderType) ||
      !OrderCalcMargin(marginOrderType,request.symbol,request.volume,request.price,requiredMargin) ||
      !MathIsValidNumber(requiredMargin) || requiredMargin<0.0 ||
      !MathIsValidNumber(snapshot.current_margin) || snapshot.current_margin<0.0 ||
      !MathIsValidNumber(snapshot.equity) || snapshot.equity<=0.0)
     {
      reason=REJECT_MARGIN_PROJECTION;
      return false;
     }
   double projectedMargin=snapshot.current_margin+requiredMargin;
   double projectedMarginLevel=(projectedMargin>0.0 ? snapshot.equity/projectedMargin*100.0 : 1.0e100);
   double projectedFreeMargin=snapshot.equity-projectedMargin;
   if(!MathIsValidNumber(projectedMargin) || !MathIsValidNumber(projectedMarginLevel) ||
      !MathIsValidNumber(projectedFreeMargin) || projectedMargin<0.0 ||
      projectedFreeMargin<=reserve ||
      (margin_floor>0.0 && projectedMarginLevel<margin_floor))
     {
      reason=REJECT_MARGIN_PROJECTION;
      return false;
     }

   bool sparse=FinancialProjectionAllZero(check);
   bool pendingZeroMarginLevelFallback=(intent.kind==STR_OP_PENDING_PLACE &&
                                        MathIsValidNumber(check.margin_level) &&
                                        check.margin_level==0.0);
   if(!sparse)
     {
      bool invalidCheckProjection=(!MathIsValidNumber(check.margin_free) ||
                                   !MathIsValidNumber(check.margin_level) ||
                                   check.margin_level<0.0 ||
                                   check.margin_free<=reserve ||
                                   (margin_floor>0.0 &&
                                    ((!pendingZeroMarginLevelFallback && check.margin_level<=0.0) ||
                                     (check.margin_level>0.0 && check.margin_level<margin_floor))));
      if(invalidCheckProjection)
        {
         string diagnosticKey=StringFormat("margin-projection-reject|%d|%d|%s",
                                           (int)intent.kind,(int)request.type,request.symbol);
         if(ShouldEmitLog(0,diagnosticKey,diagnosticKey))
            Log(0,StringFormat("Straddle: margin projection rejected local_margin=%.8f local_free_margin=%.8f local_margin_level=%.8f check_balance=%.8f check_equity=%.8f check_margin=%.8f check_free_margin=%.8f check_margin_level=%.8f operation=%s order_type=%s zero_fallback=%s",
                               projectedMargin,
                               projectedFreeMargin,
                               projectedMarginLevel,
                               check.balance,
                               check.equity,
                               check.margin,
                               check.margin_free,
                               check.margin_level,
                               EnumToString(intent.kind),
                               EnumToString(request.type),
                               BoolText(pendingZeroMarginLevelFallback)));
         reason=REJECT_MARGIN_PROJECTION;
         return false;
        }
     }

   if(snapshot.stopout_mode==ACCOUNT_STOPOUT_MODE_PERCENT)
     {
      if(snapshot.stopout_stop>0.0 && projectedMarginLevel<=snapshot.stopout_stop)
        {
         reason=REJECT_STOPOUT_PROJECTION;
         return false;
        }
      if(margin_floor>0.0 && projectedMarginLevel<margin_floor)
        {
         reason=REJECT_STOPOUT_PROJECTION;
         return false;
        }
     }
   else if(snapshot.stopout_mode==ACCOUNT_STOPOUT_MODE_MONEY)
     {
      if(projectedFreeMargin<=snapshot.stopout_stop+reserve)
        {
         reason=REJECT_STOPOUT_PROJECTION;
         return false;
        }
     }
   else
     {
      reason=REJECT_STOPOUT_PROJECTION;
      return false;
     }
   return true;
  }

bool BuildRequestForIntent(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                           MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   switch(intent.kind)
     {
      case STR_OP_PENDING_PLACE:  return BuildPendingStopRequest(intent,snapshot,request,reason);
      case STR_OP_MARKET_DEAL:    return BuildMarketDealRequest(intent,snapshot,request,reason);
      case STR_OP_SLTP:           return BuildProtectionRequest(intent,snapshot,request,reason);
      case STR_OP_PENDING_MODIFY: return BuildPendingModifyRequest(intent,snapshot,request,reason);
      case STR_OP_POSITION_REDUCE:return BuildPositionReductionRequest(intent,snapshot,request,reason);
      case STR_OP_PENDING_DELETE: return BuildPendingDeleteRequest(intent,snapshot,request,reason);
      default:
         ZeroMemory(request);
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
     }
  }

bool GateRequestForIntent(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                          const MqlTradeRequest &request,ENUM_STR_REASON_CODE &reason)
  {
   switch(intent.kind)
     {
      case STR_OP_PENDING_PLACE:
      case STR_OP_MARKET_DEAL:
         return CanOpenNewExposure(intent,snapshot,request,reason);
      case STR_OP_SLTP:
      case STR_OP_PENDING_MODIFY:
         return CanModifyProtection(intent,snapshot,request,reason);
      case STR_OP_POSITION_REDUCE:
      case STR_OP_PENDING_DELETE:
         return CanReduceOrDeleteRisk(intent,snapshot,request,reason);
      default:
         reason=REJECT_SYMBOL_CAPABILITY;
         return false;
     }
  }

string RequestFingerprint(const MqlTradeRequest &request)
  {
   return StringFormat("%d|%I64u|%I64u|%s|%.16f|%.16f|%.16f|%.16f|%.16f|%I64u|%d|%d|%d|%I64d|%s|%I64u|%I64u",
                       (int)request.action,request.magic,request.order,request.symbol,
                       request.volume,request.price,request.stoplimit,request.sl,request.tp,
                       request.deviation,(int)request.type,(int)request.type_filling,
                       (int)request.type_time,(long)request.expiration,request.comment,
                       request.position,request.position_by);
  }

bool SnapshotStillValid(const StrTradeIntent &intent,const StrTradeSnapshot &snapshot,
                        const MqlTradeRequest &request)
  {
   StrTradeSnapshot fresh;
   ENUM_STR_REASON_CODE freshReason=STR_REASON_NONE;
   ZeroMemory(fresh);
   if(!CaptureTradeSnapshot(intent,fresh,freshReason))
      return false;
   double epsilon=1.0e-10;
   if(fresh.account_trade_allowed!=snapshot.account_trade_allowed ||
      fresh.account_expert_allowed!=snapshot.account_expert_allowed ||
      fresh.terminal_trade_allowed!=snapshot.terminal_trade_allowed ||
      fresh.mql_trade_allowed!=snapshot.mql_trade_allowed)
      return false;

   switch(intent.kind)
     {
      case STR_OP_PENDING_PLACE:
         if(fresh.trade_mode!=snapshot.trade_mode || fresh.order_mode!=snapshot.order_mode ||
            fresh.expiration_mode!=snapshot.expiration_mode ||
            MathAbs(fresh.bid-snapshot.bid)>epsilon || MathAbs(fresh.ask-snapshot.ask)>epsilon ||
            MathAbs(fresh.point-snapshot.point)>epsilon || fresh.digits!=snapshot.digits ||
            fresh.stops_level!=snapshot.stops_level || fresh.freeze_level!=snapshot.freeze_level ||
            MathAbs(fresh.volume_min-snapshot.volume_min)>epsilon ||
            MathAbs(fresh.volume_max-snapshot.volume_max)>epsilon ||
            MathAbs(fresh.volume_step-snapshot.volume_step)>epsilon ||
            MathAbs(fresh.volume_limit-snapshot.volume_limit)>epsilon ||
            MathAbs(fresh.directional_volume-snapshot.directional_volume)>epsilon ||
            MathAbs(fresh.equity-snapshot.equity)>epsilon ||
            MathAbs(fresh.current_margin-snapshot.current_margin)>epsilon ||
            MathAbs(fresh.current_free_margin-snapshot.current_free_margin)>epsilon ||
            MathAbs(fresh.stopout_stop-snapshot.stopout_stop)>epsilon ||
            fresh.stopout_mode!=snapshot.stopout_mode || fresh.margin_mode!=snapshot.margin_mode ||
            fresh.account_order_limit!=snapshot.account_order_limit ||
            fresh.pending_count!=snapshot.pending_count || fresh.hedging!=snapshot.hedging ||
            fresh.hedge_allowed!=snapshot.hedge_allowed)
            return false;
         break;
      case STR_OP_MARKET_DEAL:
        {
         bool protectionGeometry=(request.sl!=0.0 || request.tp!=0.0);
         double oldExecutable=(IsBuyOrderType(request.type) ? snapshot.ask : snapshot.bid);
         double newExecutable=(IsBuyOrderType(request.type) ? fresh.ask : fresh.bid);
         double oldProtection=(IsBuyOrderType(request.type) ? snapshot.bid : snapshot.ask);
         double newProtection=(IsBuyOrderType(request.type) ? fresh.bid : fresh.ask);
         if(fresh.trade_mode!=snapshot.trade_mode || fresh.execution_mode!=snapshot.execution_mode ||
            fresh.filling_mode!=snapshot.filling_mode || fresh.order_mode!=snapshot.order_mode ||
            MathAbs(newExecutable-oldExecutable)>epsilon ||
            (protectionGeometry && MathAbs(newProtection-oldProtection)>epsilon) ||
            MathAbs(fresh.point-snapshot.point)>epsilon || fresh.digits!=snapshot.digits ||
            fresh.stops_level!=snapshot.stops_level || fresh.freeze_level!=snapshot.freeze_level ||
            MathAbs(fresh.volume_min-snapshot.volume_min)>epsilon ||
            MathAbs(fresh.volume_max-snapshot.volume_max)>epsilon ||
            MathAbs(fresh.volume_step-snapshot.volume_step)>epsilon ||
            MathAbs(fresh.volume_limit-snapshot.volume_limit)>epsilon ||
            MathAbs(fresh.directional_volume-snapshot.directional_volume)>epsilon ||
            MathAbs(fresh.equity-snapshot.equity)>epsilon ||
            MathAbs(fresh.current_margin-snapshot.current_margin)>epsilon ||
            MathAbs(fresh.current_free_margin-snapshot.current_free_margin)>epsilon ||
            MathAbs(fresh.stopout_stop-snapshot.stopout_stop)>epsilon ||
            fresh.stopout_mode!=snapshot.stopout_mode || fresh.margin_mode!=snapshot.margin_mode ||
            fresh.hedging!=snapshot.hedging || fresh.hedge_allowed!=snapshot.hedge_allowed)
            return false;
         break;
        }
      case STR_OP_POSITION_REDUCE:
        {
         double oldExecutable=(IsBuyOrderType(request.type) ? snapshot.ask : snapshot.bid);
         double newExecutable=(IsBuyOrderType(request.type) ? fresh.ask : fresh.bid);
         if(fresh.trade_mode!=snapshot.trade_mode || fresh.execution_mode!=snapshot.execution_mode ||
            fresh.filling_mode!=snapshot.filling_mode || fresh.fifo_close!=snapshot.fifo_close ||
            MathAbs(fresh.point-snapshot.point)>epsilon ||
            MathAbs(newExecutable-oldExecutable)>epsilon)
            return false;
         break;
        }
      case STR_OP_SLTP:
        {
         bool geometry=(request.sl!=0.0 || request.tp!=0.0);
         double oldPrice=(IsBuyOrderType(intent.order_type) ? snapshot.bid : snapshot.ask);
         double newPrice=(IsBuyOrderType(intent.order_type) ? fresh.bid : fresh.ask);
         if(fresh.trade_mode!=snapshot.trade_mode || fresh.order_mode!=snapshot.order_mode ||
            MathAbs(fresh.point-snapshot.point)>epsilon || fresh.digits!=snapshot.digits ||
            (geometry && (MathAbs(newPrice-oldPrice)>epsilon ||
                          fresh.stops_level!=snapshot.stops_level ||
                          fresh.freeze_level!=snapshot.freeze_level)))
            return false;
         break;
        }
      case STR_OP_PENDING_MODIFY:
         if(fresh.trade_mode!=snapshot.trade_mode || fresh.order_mode!=snapshot.order_mode ||
            (intent.change_time_policy && fresh.expiration_mode!=snapshot.expiration_mode) ||
            MathAbs(fresh.bid-snapshot.bid)>epsilon || MathAbs(fresh.ask-snapshot.ask)>epsilon ||
            MathAbs(fresh.point-snapshot.point)>epsilon || fresh.digits!=snapshot.digits ||
            fresh.stops_level!=snapshot.stops_level || fresh.freeze_level!=snapshot.freeze_level)
            return false;
         break;
      case STR_OP_PENDING_DELETE:
         // Identity, ownership, permissions and current deletability are re-gated below.
         break;
      default:
         return false;
     }
   ENUM_STR_REASON_CODE gateReason=STR_REASON_NONE;
   if(!GateRequestForIntent(intent,fresh,request,gateReason))
      return false;
   return true;
  }

string PendingActionIdentity(const StrTradeIntent &intent)
  {
   return StringFormat("%d|%s|%I64d|%d|%I64u|%I64u|%I64u|%.8f|%.8f|%.8f|%.8f|%.8f|%s",
                       (int)intent.kind,intent.symbol,intent.magic,(int)intent.order_type,
                       intent.order,intent.position,intent.position_identifier,
                       intent.requested_volume,intent.fixed_price,intent.sl,intent.tp,
                       intent.stoplimit,intent.comment);
  }

bool SamePendingActionTarget(const StrPendingAction &action,const StrTradeIntent &intent)
  {
   return (action.active && action.intent.kind==intent.kind &&
           action.intent.symbol==intent.symbol &&
           ((intent.position>0 && action.intent.position==intent.position) ||
            (intent.order>0 && action.intent.order==intent.order) ||
            (intent.position==0 && intent.order==0 && action.intent.comment==intent.comment)));
  }

int FindPendingActionTarget(const StrTradeIntent &intent)
  {
   for(int i=0;i<STR_MAX_PENDING_ACTIONS;i++)
      if(SamePendingActionTarget(g_pendingActions[i],intent))
         return i;
   return -1;
  }

bool TakeTerminalActionOutcome(const StrTradeIntent &intent,StrActionOutcome &outcome)
  {
   int slot=FindPendingActionTarget(intent);
   if(slot<0 || !g_pendingActions[slot].terminal_ready ||
      g_pendingActions[slot].terminal_consumed)
      return false;
   outcome=g_pendingActions[slot].outcome;
   g_pendingActions[slot].terminal_consumed=true;
   g_pendingActions[slot].terminal_ready=false;
   if(outcome.state==PARTIAL && g_pendingActions[slot].terminal_short_fill &&
      outcome.remaining_volume>VolumeTolerance(intent.symbol))
     {
      // Keep the exact action/generation slot as a one-shot remainder grant.
      g_pendingActions[slot].remainder_authorized=true;
      g_pendingActions[slot].updated_at=TimeCurrent();
     }
   else
      ReleasePendingActionReservation(slot);
   return true;
  }

ENUM_STR_PENDING_RESERVATION_RESULT ReservePendingAction(const StrTradeIntent &intent,
                                                          const MqlTradeRequest &request,
                                                          const ulong generation,
                                                          int &slot)
  {
   slot=-1;
   string identity=PendingActionIdentity(intent);
   int freeSlot=-1;
   for(int i=0;i<STR_MAX_PENDING_ACTIONS;i++)
     {
      if(g_pendingActions[i].active)
        {
         bool sameTarget=SamePendingActionTarget(g_pendingActions[i],intent);
         if(sameTarget && g_pendingActions[i].remainder_authorized)
           {
            // The ordinary state machine consumed a terminal PARTIAL outcome.
            // Reuse (never duplicate) its slot for the already-normalized remainder.
            StrPendingAction lifetime=g_pendingActions[i];
            ZeroMemory(g_pendingActions[i]);
            g_pendingActions[i].active=true;
            g_pendingActions[i].reserved_before_send=true;
            g_pendingActions[i].identity=identity;
            g_pendingActions[i].intent=intent;
            g_pendingActions[i].generation=lifetime.generation;
            g_pendingActions[i].updated_at=TimeCurrent();
            g_pendingActions[i].original_requested_volume=lifetime.original_requested_volume;
            g_pendingActions[i].current_leg_requested_volume=intent.requested_volume;
            g_pendingActions[i].cumulative_confirmed=lifetime.cumulative_confirmed;
            g_pendingActions[i].confirmed_profit=lifetime.confirmed_profit;
            g_pendingActions[i].confirmed_commission=lifetime.confirmed_commission;
            g_pendingActions[i].confirmed_swap=lifetime.confirmed_swap;
            g_pendingActions[i].confirmed_fee=lifetime.confirmed_fee;
            g_pendingActions[i].initial_deal=lifetime.initial_deal;
            g_pendingActions[i].deal=lifetime.deal;
            g_pendingActions[i].position_id=lifetime.position_id;
            g_pendingActions[i].expected_type=request.type;
            g_pendingActions[i].expected_price=request.price;
            g_pendingActions[i].expected_volume=request.volume;
            g_pendingActions[i].expected_stoplimit=request.stoplimit;
            g_pendingActions[i].expected_sl=request.sl;
            g_pendingActions[i].expected_tp=request.tp;
            g_pendingActions[i].expected_time_type=request.type_time;
            g_pendingActions[i].expected_expiration=request.expiration;
            slot=i;
            return STR_PENDING_RESERVATION_OK;
           }
         if(g_pendingActions[i].identity==identity || sameTarget)
           {
            slot=i;
            return STR_PENDING_RESERVATION_ALREADY_TRACKED;
           }
        }
      else if(freeSlot<0)
         freeSlot=i;
     }
   if(freeSlot<0)
      return STR_PENDING_RESERVATION_FULL;
   ZeroMemory(g_pendingActions[freeSlot]);
   g_pendingActions[freeSlot].active=true;
   g_pendingActions[freeSlot].reserved_before_send=true;
   g_pendingActions[freeSlot].identity=identity;
   g_pendingActions[freeSlot].intent=intent;
   g_pendingActions[freeSlot].original_requested_volume=intent.requested_volume;
   g_pendingActions[freeSlot].current_leg_requested_volume=intent.requested_volume;
   g_pendingActions[freeSlot].expected_type=request.type;
   g_pendingActions[freeSlot].expected_price=request.price;
   g_pendingActions[freeSlot].expected_volume=request.volume;
   g_pendingActions[freeSlot].expected_stoplimit=request.stoplimit;
   g_pendingActions[freeSlot].expected_sl=request.sl;
   g_pendingActions[freeSlot].expected_tp=request.tp;
   g_pendingActions[freeSlot].expected_time_type=request.type_time;
   g_pendingActions[freeSlot].expected_expiration=request.expiration;
   g_pendingActions[freeSlot].generation=generation;
   g_pendingActions[freeSlot].updated_at=TimeCurrent();
   slot=freeSlot;
   return STR_PENDING_RESERVATION_OK;
  }

bool CommitPendingActionReservation(const int slot,const StrActionOutcome &outcome)
  {
   if(slot<0 || slot>=STR_MAX_PENDING_ACTIONS || !g_pendingActions[slot].active ||
      !g_pendingActions[slot].reserved_before_send)
      return false;
   g_pendingActions[slot].outcome=outcome;
   // result.volume is provisional send metadata. Immutable deal history is the
   // sole cumulative accounting source, so never seed or reset the lifetime
   // accumulator here (a remainder leg inherits prior confirmed accounting).
   g_pendingActions[slot].provisional_result_volume=outcome.confirmed_volume;
   g_pendingActions[slot].outcome.requested_volume=
      g_pendingActions[slot].original_requested_volume;
   g_pendingActions[slot].outcome.confirmed_volume=
      g_pendingActions[slot].cumulative_confirmed;
   g_pendingActions[slot].outcome.confirmed_net_money=
      g_pendingActions[slot].confirmed_profit+
      g_pendingActions[slot].confirmed_commission+
      g_pendingActions[slot].confirmed_swap+
      g_pendingActions[slot].confirmed_fee;
   if(g_pendingActions[slot].outcome.deal==0)
      g_pendingActions[slot].outcome.deal=g_pendingActions[slot].deal;
   g_pendingActions[slot].outcome.remaining_volume=NormalizeVolumeDown(
      g_pendingActions[slot].intent.symbol,
      MathMax(0.0,g_pendingActions[slot].original_requested_volume-
                    g_pendingActions[slot].cumulative_confirmed));
   if(outcome.order>0) g_pendingActions[slot].order=outcome.order;
   if(outcome.deal>0)  g_pendingActions[slot].deal=outcome.deal;
   g_pendingActions[slot].position_id=g_pendingActions[slot].intent.position_identifier;
   g_pendingActions[slot].updated_at=TimeCurrent();
   return true;
  }

void ReleasePendingActionReservation(const int slot)
  {
   if(slot<0 || slot>=STR_MAX_PENDING_ACTIONS)
      return;
   ZeroMemory(g_pendingActions[slot]);
  }

int FindPendingAction(const uint requestId,const ulong order,const ulong deal,
                      const ulong positionId,const string identity)
  {
   for(int i=0;i<STR_MAX_PENDING_ACTIONS;i++)
     {
      if(!g_pendingActions[i].active)
         continue;
      if((requestId>0 && g_pendingActions[i].outcome.request_id==requestId) ||
         (order>0 && g_pendingActions[i].order==order) ||
         (deal>0 && g_pendingActions[i].deal==deal) ||
         (positionId>0 && (g_pendingActions[i].position_id==positionId ||
                           g_pendingActions[i].intent.position==positionId)) ||
         (identity!="" && g_pendingActions[i].identity==identity))
         return i;
     }
   return -1;
  }

bool SeenTransaction(const string identity)
  {
   if(identity=="") return false;
   for(int i=0;i<STR_SEEN_TX_CAPACITY;i++)
      if(g_seenTransactions[i].used && g_seenTransactions[i].identity==identity)
         return true;
   return false;
  }

void RecordSeenTransaction(const string identity)
  {
   if(identity=="") return;
   g_seenTransactions[g_seenTransactionCursor].used=true;
   g_seenTransactions[g_seenTransactionCursor].identity=identity;
   g_seenTransactionCursor=(g_seenTransactionCursor+1)%STR_SEEN_TX_CAPACITY;
  }

bool SeenDealAccounting(const ulong deal)
  {
   if(deal==0) return false;
   for(int i=0;i<STR_SEEN_DEAL_CAPACITY;i++)
      if(g_seenDeals[i].used && g_seenDeals[i].deal_id==deal)
         return true;
   return false;
  }

void RecordSeenDealAccounting(const ulong deal)
  {
   if(deal==0) return;
   g_seenDeals[g_seenDealCursor].used=true;
   g_seenDeals[g_seenDealCursor].deal_id=deal;
   g_seenDealCursor=(g_seenDealCursor+1)%STR_SEEN_DEAL_CAPACITY;
  }

bool ConfirmedDealAccounting(const ulong deal,double &volume,double &profit,
                             double &commission,double &swap,double &fee,
                             ulong &positionId)
  {
   volume=0.0; profit=0.0; commission=0.0; swap=0.0; fee=0.0; positionId=0;
   if(deal==0 || !HistoryDealSelect(deal)) return false;
   volume=HistoryDealGetDouble(deal,DEAL_VOLUME);
   profit=HistoryDealGetDouble(deal,DEAL_PROFIT);
   commission=HistoryDealGetDouble(deal,DEAL_COMMISSION);
   swap=HistoryDealGetDouble(deal,DEAL_SWAP);
   fee=HistoryDealGetDouble(deal,DEAL_FEE);
   positionId=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
   return MathIsValidNumber(volume) && volume>=0.0 &&
          MathIsValidNumber(profit) && MathIsValidNumber(commission) &&
          MathIsValidNumber(swap) && MathIsValidNumber(fee);
  }

string TransactionIdentity(const MqlTradeTransaction &trans,
                           const MqlTradeRequest &request,
                           const MqlTradeResult &result)
  {
   long dealTimeMsc=0;
   if(trans.deal>0 && HistoryDealSelect(trans.deal))
      dealTimeMsc=(long)HistoryDealGetInteger(trans.deal,DEAL_TIME_MSC);
   return StringFormat("%d|%I64u|%I64u|%I64u|%u|%u|%.8f|%I64d",
                       (int)trans.type,trans.order,trans.deal,trans.position,
                       result.request_id,result.retcode,trans.volume,dealTimeMsc);
  }

StrActionOutcome MakeActionOutcome(const StrTradeIntent &intent,
                                   ENUM_STR_ACTION_STATE state,
                                   ENUM_STR_REASON_CODE reason,
                                   const MqlTradeResult &result,
                                   bool terminalResult,int lastError)
  {
   StrActionOutcome outcome;
   ZeroMemory(outcome);
   outcome.kind=intent.kind;
   outcome.reason=reason;
   outcome.state=state;
   outcome.request_id=result.request_id;
   outcome.requested_volume=intent.requested_volume;
   double confirmed=NormalizeVolumeDown(intent.symbol,MathMax(0.0,result.volume));
   confirmed=MathMin(confirmed,NormalizeVolumeDown(intent.symbol,intent.requested_volume));
   outcome.confirmed_volume=confirmed;
   outcome.remaining_volume=NormalizeVolumeDown(intent.symbol,
                                                MathMax(0.0,intent.requested_volume-confirmed));
   outcome.retcode=result.retcode;
   outcome.retcode_external=result.retcode_external;
   outcome.order=result.order;
   outcome.deal=result.deal;
   outcome.result_price=result.price;
   outcome.result_comment=result.comment;
   outcome.terminal_result=terminalResult;
   outcome.last_error=lastError;
   return outcome;
  }

StrActionOutcome ClassifyTradeResult(const StrTradeIntent &intent,
                                     const MqlTradeResult &result,
                                     bool terminalResult,int lastError)
  {
   if(!terminalResult)
     {
      ENUM_RETCODE_ACTION failedAction=ClassifyRetcode(result.retcode);
      bool deferred=(failedAction==ACT_RETRY_REFRESH || failedAction==ACT_BACKOFF ||
                     failedAction==ACT_SKIP_WAIT || result.retcode==TRADE_RETCODE_TIMEOUT);
      return MakeActionOutcome(intent,(deferred ? DEFERRED : REJECTED),
                               (deferred ? DEFER_TRANSACTION_RECONCILIATION : REJECT_ORDERCHECK_RETCODE),
                               result,terminalResult,lastError);
     }
   StrActionOutcome volumeOutcome=MakeActionOutcome(intent,PENDING_RECONCILIATION,
                                                     DEFER_TRANSACTION_RECONCILIATION,
                                                     result,terminalResult,lastError);
   if(result.retcode==TRADE_RETCODE_DONE_PARTIAL)
     {
      if(volumeOutcome.confirmed_volume>0.0)
        {
         volumeOutcome.state=PARTIAL;
         volumeOutcome.reason=PARTIAL_VOLUME_CONFIRMED;
        }
      return volumeOutcome;
     }
   if(result.retcode==TRADE_RETCODE_PLACED)
      return volumeOutcome;
   if(result.retcode==TRADE_RETCODE_DONE)
     {
      // A successful send retcode is not a completion proof. Deal operations
      // wait for immutable history; SLTP/delete/modify wait for exact live-state
      // verification against the request stored in the pre-send reservation.
      if(volumeOutcome.confirmed_volume>0.0)
        {
         volumeOutcome.state=PARTIAL;
         volumeOutcome.reason=PARTIAL_VOLUME_CONFIRMED;
        }
      return volumeOutcome;
     }
   if(result.retcode==TRADE_RETCODE_TIMEOUT ||
      (terminalResult && result.retcode==0))
      return MakeActionOutcome(intent,PENDING_RECONCILIATION,DEFER_TRANSACTION_RECONCILIATION,
                               result,terminalResult,lastError);
   ENUM_RETCODE_ACTION action=ClassifyRetcode(result.retcode);
   if(action==ACT_RETRY_REFRESH || action==ACT_BACKOFF || action==ACT_SKIP_WAIT)
      return MakeActionOutcome(intent,DEFERRED,
                               (action==ACT_SKIP_WAIT ? DEFER_MARKET_CLOSED :
                                (action==ACT_BACKOFF ? DEFER_SERVER_BUSY : DEFER_PRICE_CHANGED)),
                               result,terminalResult,lastError);
   return MakeActionOutcome(intent,REJECTED,
                            (action==ACT_ABORT_CYCLE ? REJECT_EXPOSURE_CAP : REJECT_ORDERCHECK_RETCODE),
                            result,terminalResult,lastError);
  }

StrActionOutcome MakeDeferredPriceChanged(const StrTradeIntent &intent)
  {
   MqlTradeResult result;
   ZeroMemory(result);
   return MakeActionOutcome(intent,DEFERRED,DEFER_PRICE_CHANGED,result,false,0);
  }

bool PendingActionLiveCompletionConfirmed(const int slot)
  {
   if(slot<0 || slot>=STR_MAX_PENDING_ACTIONS || !g_pendingActions[slot].active)
      return false;
   StrTradeIntent intent=g_pendingActions[slot].intent;
   double point=SymbolInfoDouble(intent.symbol,SYMBOL_POINT);
   if(!MathIsValidNumber(point) || point<=0.0) point=1.0e-8;
   double tolerance=point*0.5;
   if(intent.kind==STR_OP_PENDING_DELETE)
      return !OrderSelect(intent.order);
   if(intent.kind==STR_OP_SLTP)
     {
      if(!PositionSelectByTicket(intent.position)) return false;
      if((ulong)PositionGetInteger(POSITION_IDENTIFIER)!=intent.position_identifier ||
         PositionGetInteger(POSITION_MAGIC)!=intent.magic ||
         PositionGetString(POSITION_SYMBOL)!=intent.symbol) return false;
      return (MathAbs(PositionGetDouble(POSITION_SL)-g_pendingActions[slot].expected_sl)<=tolerance &&
              MathAbs(PositionGetDouble(POSITION_TP)-g_pendingActions[slot].expected_tp)<=tolerance);
     }
   if(intent.kind==STR_OP_PENDING_PLACE || intent.kind==STR_OP_PENDING_MODIFY)
     {
      ulong ticket=(intent.kind==STR_OP_PENDING_PLACE ? g_pendingActions[slot].order : intent.order);
      if(ticket==0 || !OrderSelect(ticket)) return false;
      if(OrderGetInteger(ORDER_MAGIC)!=intent.magic ||
         OrderGetString(ORDER_SYMBOL)!=intent.symbol ||
         (ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE)!=g_pendingActions[slot].expected_type)
         return false;
      bool volumeMatches=(intent.kind==STR_OP_PENDING_MODIFY ||
                          MathAbs(OrderGetDouble(ORDER_VOLUME_INITIAL)-
                                  g_pendingActions[slot].expected_volume)<=
                             VolumeTolerance(intent.symbol));
      if(!volumeMatches ||
         MathAbs(OrderGetDouble(ORDER_PRICE_OPEN)-g_pendingActions[slot].expected_price)>tolerance ||
         MathAbs(OrderGetDouble(ORDER_PRICE_STOPLIMIT)-g_pendingActions[slot].expected_stoplimit)>tolerance ||
         MathAbs(OrderGetDouble(ORDER_SL)-g_pendingActions[slot].expected_sl)>tolerance ||
         MathAbs(OrderGetDouble(ORDER_TP)-g_pendingActions[slot].expected_tp)>tolerance ||
         (ENUM_ORDER_TYPE_TIME)OrderGetInteger(ORDER_TYPE_TIME)!=g_pendingActions[slot].expected_time_type ||
         (datetime)OrderGetInteger(ORDER_TIME_EXPIRATION)!=g_pendingActions[slot].expected_expiration)
         return false;
      return true;
     }
   return false;
  }

StrActionOutcome CheckAndSendRequest(const StrTradeIntent &requestedIntent)
  {
   StrTradeIntent intent=requestedIntent;
   StrActionOutcome terminalOutcome;
   ZeroMemory(terminalOutcome);
   if(TakeTerminalActionOutcome(intent,terminalOutcome))
      return terminalOutcome;
   int remainderSlot=FindPendingActionTarget(intent);
   if(remainderSlot>=0 && g_pendingActions[remainderSlot].remainder_authorized)
     {
      double remainder=NormalizeVolumeDown(intent.symbol,
         g_pendingActions[remainderSlot].outcome.remaining_volume);
      if(remainder<=VolumeTolerance(intent.symbol))
         return g_pendingActions[remainderSlot].outcome;
      intent.requested_volume=remainder;
     }
   if(g_pendingRegistryFault)
     {
      MqlTradeResult faultResult;
      ZeroMemory(faultResult);
      return MakeActionOutcome(intent,PENDING_RECONCILIATION,
                               DEFER_TRANSACTION_RECONCILIATION,faultResult,false,0);
     }
   for(int attempt=0;attempt<=g_validatedMaxRetries;attempt++)
     {
      StrTradeSnapshot snapshot;
      MqlTradeRequest finalRequest;
      MqlTradeCheckResult check;
      MqlTradeResult result;
      ENUM_STR_REASON_CODE reason=STR_REASON_NONE;
      ZeroMemory(snapshot);
      ZeroMemory(finalRequest);
      ZeroMemory(check);
      ZeroMemory(result);

      if(!CaptureTradeSnapshot(intent,snapshot,reason))
         return MakeActionOutcome(intent,
                                  (reason==DEFER_MARKET_CLOSED ? DEFERRED : REJECTED),
                                  reason,result,false,GetLastError());
      if(!BuildRequestForIntent(intent,snapshot,finalRequest,reason))
         return MakeActionOutcome(intent,REJECTED,reason,result,false,GetLastError());
      if(!GateRequestForIntent(intent,snapshot,finalRequest,reason))
         return MakeActionOutcome(intent,
                                  (reason==DEFER_PENDING_DELETE || reason==DEFER_PRICE_CHANGED ||
                                   reason==DEFER_MARKET_CLOSED || reason==DEFER_SERVER_BUSY
                                   ? DEFERRED : REJECTED),
                                  reason,result,false,GetLastError());

      string checkedFingerprint=RequestFingerprint(finalRequest);
      ResetLastError();
      if(!OrderCheck(finalRequest,check))
        {
         result.retcode=check.retcode;
         result.comment=check.comment;
         return MakeActionOutcome(intent,REJECTED,REJECT_ORDERCHECK_FALSE,result,false,GetLastError());
        }
      int checkError=GetLastError();
      result.retcode=check.retcode;
      result.comment=check.comment;
      Log(2,StringFormat("Straddle: canonical OrderCheck kind=%d retcode=%u comment=%s",
                         (int)intent.kind,check.retcode,check.comment));
      bool sparseCheckMetadata=(check.retcode==0);
      if(!sparseCheckMetadata && check.retcode!=TRADE_RETCODE_DONE &&
         check.retcode!=TRADE_RETCODE_PLACED)
        {
         ENUM_RETCODE_ACTION checkAction=ClassifyRetcode(check.retcode);
         if(checkAction==ACT_RETRY_REFRESH || checkAction==ACT_BACKOFF || checkAction==ACT_SKIP_WAIT)
           {
            ENUM_STR_REASON_CODE deferredReason=(checkAction==ACT_SKIP_WAIT ? DEFER_MARKET_CLOSED :
                                                 (checkAction==ACT_BACKOFF ? DEFER_SERVER_BUSY : DEFER_PRICE_CHANGED));
            return MakeActionOutcome(intent,DEFERRED,deferredReason,result,true,checkError);
           }
         return MakeActionOutcome(intent,REJECTED,REJECT_ORDERCHECK_RETCODE,result,true,checkError);
        }
      if(!ValidateProjectedMarginAndStopout(intent,snapshot,finalRequest,check,reason))
         return MakeActionOutcome(intent,REJECTED,reason,result,true,checkError);

      if(!SnapshotStillValid(intent,snapshot,finalRequest))
         continue;
      if(RequestFingerprint(finalRequest)!=checkedFingerprint)
         return MakeActionOutcome(intent,DEFERRED,DEFER_PRICE_CHANGED,result,false,0);

      int reservationSlot=-1;
      ENUM_STR_PENDING_RESERVATION_RESULT reservation=
         ReservePendingAction(intent,finalRequest,snapshot.generation,reservationSlot);
      if(reservation==STR_PENDING_RESERVATION_FULL)
         return MakeActionOutcome(intent,DEFERRED,DEFER_SERVER_BUSY,result,false,0);
      if(reservation==STR_PENDING_RESERVATION_ALREADY_TRACKED)
        {
         StrActionOutcome tracked=g_pendingActions[reservationSlot].outcome;
         if(tracked.kind!=intent.kind)
            tracked=MakeActionOutcome(intent,PENDING_RECONCILIATION,
                                      DEFER_TRANSACTION_RECONCILIATION,result,false,0);
         tracked.state=PENDING_RECONCILIATION;
         tracked.reason=DEFER_TRANSACTION_RECONCILIATION;
         return tracked;
        }

      ZeroMemory(result);
      ResetLastError();
      bool sent=OrderSend(finalRequest,result);
      int sendError=GetLastError();
      StrActionOutcome outcome=ClassifyTradeResult(intent,result,sent,sendError);
      if(outcome.state==PARTIAL || outcome.state==PENDING_RECONCILIATION)
        {
         if(!CommitPendingActionReservation(reservationSlot,outcome))
           {
            g_pendingRegistryFault=true;
            outcome.state=PENDING_RECONCILIATION;
            outcome.reason=DEFER_TRANSACTION_RECONCILIATION;
           }
         else if(result.deal>0 && !SeenDealAccounting(result.deal))
           {
            double volume=0.0,profit=0.0,commission=0.0,swap=0.0,fee=0.0;
            ulong positionId=0;
            if(ConfirmedDealAccounting(result.deal,volume,profit,commission,swap,fee,positionId))
              {
               double requested=NormalizeVolumeDown(intent.symbol,
                  g_pendingActions[reservationSlot].original_requested_volume);
               g_pendingActions[reservationSlot].cumulative_confirmed=
                  MathMin(requested,NormalizeVolumeDown(intent.symbol,
                     g_pendingActions[reservationSlot].cumulative_confirmed+
                     MathMax(0.0,volume)));
               g_pendingActions[reservationSlot].confirmed_profit+=profit;
               g_pendingActions[reservationSlot].confirmed_commission+=commission;
               g_pendingActions[reservationSlot].confirmed_swap+=swap;
               g_pendingActions[reservationSlot].confirmed_fee+=fee;
               g_pendingActions[reservationSlot].deal=result.deal;
               if(g_pendingActions[reservationSlot].initial_deal==0)
                  g_pendingActions[reservationSlot].initial_deal=result.deal;
               g_pendingActions[reservationSlot].position_id=positionId;
               outcome.requested_volume=requested;
               outcome.confirmed_volume=g_pendingActions[reservationSlot].cumulative_confirmed;
               outcome.remaining_volume=NormalizeVolumeDown(intent.symbol,
                  MathMax(0.0,requested-outcome.confirmed_volume));
               outcome.confirmed_net_money=
                  g_pendingActions[reservationSlot].confirmed_profit+
                  g_pendingActions[reservationSlot].confirmed_commission+
                  g_pendingActions[reservationSlot].confirmed_swap+
                  g_pendingActions[reservationSlot].confirmed_fee;
               if(outcome.remaining_volume<=VolumeTolerance(intent.symbol))
                 {
                  outcome.state=COMPLETED;
                  outcome.reason=STR_REASON_NONE;
                 }
               else if(outcome.confirmed_volume>0.0)
                 {
                  outcome.state=PARTIAL;
                  outcome.reason=PARTIAL_VOLUME_CONFIRMED;
                 }
               g_pendingActions[reservationSlot].outcome=outcome;
               RecordSeenDealAccounting(result.deal);
               g_bookDirty=true;
               Log(2,StringFormat("Straddle: immediate confirmed action accounting kind=%d volume=%.2f net=%.2f deal=%I64u",
                                  (int)intent.kind,outcome.confirmed_volume,
                                  outcome.confirmed_net_money,result.deal));
               if(outcome.state==COMPLETED)
                  ReleasePendingActionReservation(reservationSlot);
              }
           }
         else if(PendingActionLiveCompletionConfirmed(reservationSlot))
           {
            outcome.state=COMPLETED;
            outcome.reason=STR_REASON_NONE;
            ReleasePendingActionReservation(reservationSlot);
           }
         return outcome;
        }
      ReleasePendingActionReservation(reservationSlot);
      return outcome;
     }
   return MakeDeferredPriceChanged(intent);
  }

bool TradeOutcomeAccepted(const StrActionOutcome &outcome)
  {
   // Production callers may mutate cycle/build state only after a terminally
   // confirmed action. PARTIAL and PENDING_RECONCILIATION remain owned by the
   // fixed pending-action registry until transaction reconciliation resolves.
   return (outcome.state==COMPLETED);
  }

void RegisterFloatIdentity(const MqlTradeTransaction &trans)
  {
   if(trans.deal==0 || !HistoryDealSelect(trans.deal))
      return;
   ulong positionId=(ulong)HistoryDealGetInteger(trans.deal,DEAL_POSITION_ID);
   if(positionId==0)
      return;
   if((trans.position>0 && IsFloatTicket(trans.position)) || IsFloatPositionId(positionId))
      AddFloatClosedPositionId(positionId);
  }

bool PendingActionTerminalShortFill(const int slot,const MqlTradeTransaction &trans)
  {
   if(slot<0 || slot>=STR_MAX_PENDING_ACTIONS ||
      g_pendingActions[slot].outcome.state!=PARTIAL)
      return false;
   ulong order=(trans.order>0 ? trans.order : g_pendingActions[slot].order);
   if(order>0 && OrderSelect(order))
     {
      ENUM_ORDER_STATE liveState=(ENUM_ORDER_STATE)OrderGetInteger(ORDER_STATE);
      if(liveState==ORDER_STATE_STARTED || liveState==ORDER_STATE_PLACED ||
         liveState==ORDER_STATE_PARTIAL || liveState==ORDER_STATE_REQUEST_ADD ||
         liveState==ORDER_STATE_REQUEST_MODIFY || liveState==ORDER_STATE_REQUEST_CANCEL)
         return false;
     }
   if(order>0 && HistoryOrderSelect(order))
     {
      ENUM_ORDER_STATE historyState=(ENUM_ORDER_STATE)HistoryOrderGetInteger(order,ORDER_STATE);
      return (historyState==ORDER_STATE_FILLED || historyState==ORDER_STATE_CANCELED ||
              historyState==ORDER_STATE_REJECTED || historyState==ORDER_STATE_EXPIRED);
     }
   return (trans.type==TRADE_TRANSACTION_HISTORY_ADD ||
           trans.type==TRADE_TRANSACTION_ORDER_DELETE);
  }

void ReconcileTradeTransaction(const MqlTradeTransaction &trans,
                               const MqlTradeRequest &request,
                               const MqlTradeResult &result,
                               const int maxWork)
  {
   if(maxWork<=0)
      return;
   // A deal transaction can arrive before its immutable history row. Do not
   // consume the event identity until that row is selectable; a later broker
   // callback must be able to retry the same event safely.
   if(trans.deal>0 && !HistoryDealSelect(trans.deal))
      return;
   string eventIdentity=TransactionIdentity(trans,request,result);
   if(SeenTransaction(eventIdentity))
      return;
   RecordSeenTransaction(eventIdentity);

   // Float close identity is registered inside the same deduplicated boundary,
   // before confirmed close money can become visible to CycleRealized().
   RegisterFloatIdentity(trans);

   ulong positionId=trans.position;
   if(trans.deal>0 && HistoryDealSelect(trans.deal))
     {
      ulong dealPositionId=(ulong)HistoryDealGetInteger(trans.deal,DEAL_POSITION_ID);
      if(dealPositionId>0) positionId=dealPositionId;
     }
   string intentIdentity="";
   int slot=FindPendingAction(result.request_id,trans.order,trans.deal,positionId,intentIdentity);
   if(slot<0 && request.position>0)
      slot=FindPendingAction(result.request_id,request.order,trans.deal,request.position,intentIdentity);

   ulong deal=trans.deal;
   if(deal>0)
     {
      // Duplicate deal accounting skips only the money/volume mutation. The
      // request/order/position event still reconciles below because it may carry
      // newer linkage or terminal live-state evidence.
      for(int dealWork=0;dealWork<1;dealWork++)
        {
         if(SeenDealAccounting(deal))
            continue;
         double volume=0.0,profit=0.0,commission=0.0,swap=0.0,fee=0.0;
         ulong confirmedPositionId=0;
         if(!ConfirmedDealAccounting(deal,volume,profit,commission,swap,fee,confirmedPositionId))
            return;
         if(slot>=0)
           {
            double requested=NormalizeVolumeDown(g_pendingActions[slot].intent.symbol,
                                                  g_pendingActions[slot].original_requested_volume);
            double cumulative=NormalizeVolumeDown(g_pendingActions[slot].intent.symbol,
                                                   g_pendingActions[slot].cumulative_confirmed+
                                                   MathMax(0.0,volume));
            g_pendingActions[slot].cumulative_confirmed=MathMin(requested,cumulative);
            g_pendingActions[slot].confirmed_profit+=profit;
            g_pendingActions[slot].confirmed_commission+=commission;
            g_pendingActions[slot].confirmed_swap+=swap;
            g_pendingActions[slot].confirmed_fee+=fee;
            g_pendingActions[slot].deal=deal;
            if(g_pendingActions[slot].initial_deal==0)
               g_pendingActions[slot].initial_deal=deal;
            if(confirmedPositionId>0) g_pendingActions[slot].position_id=confirmedPositionId;
            g_pendingActions[slot].outcome.requested_volume=requested;
            g_pendingActions[slot].outcome.confirmed_volume=g_pendingActions[slot].cumulative_confirmed;
            g_pendingActions[slot].outcome.remaining_volume=
               NormalizeVolumeDown(g_pendingActions[slot].intent.symbol,
                  MathMax(0.0,requested-g_pendingActions[slot].cumulative_confirmed));
            g_pendingActions[slot].outcome.deal=deal;
            g_pendingActions[slot].outcome.confirmed_net_money=
               g_pendingActions[slot].confirmed_profit+
               g_pendingActions[slot].confirmed_commission+
               g_pendingActions[slot].confirmed_swap+
               g_pendingActions[slot].confirmed_fee;
            g_pendingActions[slot].updated_at=TimeCurrent();
            Log(2,StringFormat("Straddle: confirmed action accounting kind=%d volume=%.2f net=%.2f deal=%I64u",
                               (int)g_pendingActions[slot].intent.kind,
                               g_pendingActions[slot].cumulative_confirmed,
                               g_pendingActions[slot].outcome.confirmed_net_money,deal));
            if(g_pendingActions[slot].outcome.remaining_volume<=
               VolumeTolerance(g_pendingActions[slot].intent.symbol))
              {
               g_pendingActions[slot].outcome.state=COMPLETED;
               g_pendingActions[slot].outcome.reason=STR_REASON_NONE;
              }
            else if(g_pendingActions[slot].cumulative_confirmed>0.0)
              {
               g_pendingActions[slot].outcome.state=PARTIAL;
               g_pendingActions[slot].outcome.reason=PARTIAL_VOLUME_CONFIRMED;
              }
           }
         RecordSeenDealAccounting(deal);
         g_bookDirty=true;
        }
     }

   if(slot>=0)
     {
      if(trans.order>0) g_pendingActions[slot].order=trans.order;
      if(result.request_id>0) g_pendingActions[slot].outcome.request_id=result.request_id;
      if(PendingActionLiveCompletionConfirmed(slot))
        {
         g_pendingActions[slot].outcome.state=COMPLETED;
         g_pendingActions[slot].outcome.reason=STR_REASON_NONE;
        }
      if(g_pendingActions[slot].outcome.state==COMPLETED)
        {
         g_pendingActions[slot].terminal_ready=true;
         g_pendingActions[slot].terminal_consumed=false;
         g_pendingActions[slot].terminal_short_fill=false;
        }
      else if(PendingActionTerminalShortFill(slot,trans))
        {
         // The server is terminal but short of the requested volume. Publish
         // the exact normalized remainder once; no send occurs in this handler.
         g_pendingActions[slot].terminal_ready=true;
         g_pendingActions[slot].terminal_consumed=false;
         g_pendingActions[slot].terminal_short_fill=true;
        }
     }

   bool anyActive=false;
   for(int i=0;i<STR_MAX_PENDING_ACTIONS && i<maxWork;i++)
      if(g_pendingActions[i].active) { anyActive=true; break; }
   if(!anyActive)
      g_pendingRegistryFault=false;
  }

StrActionOutcome ExecutePositionReduction(const ulong ticket,const double volume,
                                          const string comment)
  {
   StrTradeIntent intent;
   InitializeTradeIntent(intent);
   MqlTradeResult emptyResult;
   ZeroMemory(emptyResult);
   intent.kind=STR_OP_POSITION_REDUCE;
   intent.position=ticket;
   intent.requested_volume=volume;
   intent.comment=comment;
   intent.new_exposure=false;
   StrActionOutcome terminalOutcome;
   ZeroMemory(terminalOutcome);
   if(TakeTerminalActionOutcome(intent,terminalOutcome))
      return terminalOutcome;
   if(!PositionSelectByTicket(ticket))
      return MakeActionOutcome(intent,COMPLETED,STR_REASON_NONE,emptyResult,true,0);
   ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   intent.position_identifier=(ulong)PositionGetInteger(POSITION_IDENTIFIER);
   intent.order_type=(type==POSITION_TYPE_BUY ? ORDER_TYPE_SELL : ORDER_TYPE_BUY);
   return CheckAndSendRequest(intent);
  }

StrActionOutcome ExecutePendingDelete(const ulong ticket,const string comment)
  {
   StrTradeIntent intent;
   InitializeTradeIntent(intent);
   MqlTradeResult emptyResult;
   ZeroMemory(emptyResult);
   intent.kind=STR_OP_PENDING_DELETE;
   intent.order=ticket;
   intent.comment=comment;
   intent.new_exposure=false;
   StrActionOutcome terminalOutcome;
   ZeroMemory(terminalOutcome);
   if(TakeTerminalActionOutcome(intent,terminalOutcome))
      return terminalOutcome;
   if(!OrderSelect(ticket))
      return MakeActionOutcome(intent,COMPLETED,STR_REASON_NONE,emptyResult,true,0);
   intent.order_type=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
   return CheckAndSendRequest(intent);
  }

StrActionOutcome ExecutePositionProtection(const ulong ticket,const double sl,
                                            const double tp,const string comment)
  {
   StrTradeIntent intent;
   InitializeTradeIntent(intent);
   MqlTradeResult emptyResult;
   ZeroMemory(emptyResult);
   intent.kind=STR_OP_SLTP;
   intent.position=ticket;
   intent.sl=sl;
   intent.tp=tp;
   intent.comment=comment;
   intent.new_exposure=false;
   StrActionOutcome terminalOutcome;
   ZeroMemory(terminalOutcome);
   if(TakeTerminalActionOutcome(intent,terminalOutcome))
      return terminalOutcome;
   if(!PositionSelectByTicket(ticket))
      return MakeActionOutcome(intent,COMPLETED,STR_REASON_NONE,emptyResult,true,0);
   intent.position_identifier=(ulong)PositionGetInteger(POSITION_IDENTIFIER);
   intent.order_type=(PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY ?
                      ORDER_TYPE_BUY : ORDER_TYPE_SELL);
   return CheckAndSendRequest(intent);
  }

StrActionOutcome ExecutePendingStop(const bool isBuy,const double volume,
                                    const double fixedPrice,const string comment)
  {
   StrTradeIntent intent;
   InitializeTradeIntent(intent);
   intent.kind=STR_OP_PENDING_PLACE;
   intent.order_type=(isBuy ? ORDER_TYPE_BUY_STOP : ORDER_TYPE_SELL_STOP);
   intent.requested_volume=volume;
   intent.fixed_price=fixedPrice;
   intent.comment=comment;
   intent.new_exposure=true;
   intent.margin_floor_pct=MarketValidationMinMarginLevelFloorPct();
   intent.free_margin_reserve=MoneyInput(InpMarketValidationMinFreeMarginAfterCheckUSD);
   return CheckAndSendRequest(intent);
  }

StrActionOutcome ExecuteMarketDeal(const ENUM_ORDER_TYPE orderType,const double volume,
                                   const double sl,const double tp,const string comment,
                                   const double marginFloorPct,const double freeMarginReserve)
  {
   StrTradeIntent intent;
   InitializeTradeIntent(intent);
   intent.kind=STR_OP_MARKET_DEAL;
   intent.order_type=orderType;
   intent.requested_volume=volume;
   intent.sl=sl;
   intent.tp=tp;
   intent.comment=comment;
   intent.new_exposure=true;
   intent.margin_floor_pct=MathMax(0.0,marginFloorPct);
   intent.free_margin_reserve=MathMax(0.0,freeMarginReserve);
   return CheckAndSendRequest(intent);
  }

//+------------------------------------------------------------------+
//| 0.0.40 O1: recompute ALL whole-book aggregates in ONE              |
//| PositionsTotal() pass + ONE OrdersTotal() pass. The per-position   |
//| filter/accumulation logic below is copied VERBATIM from the 13     |
//| original 0.0.39 getters (CountMyPositions / CountMyPendings /      |
//| OpenFloatingPL / NetExposureLots / CountRescueHedges /             |
//| CountTrendRescueEntries[/ForDirection] / CountAveragingEntries /   |
//| AveragingTotalLots / AveragingCoreExposureLots /                   |
//| AveragingCoreSideFloating / TrendRescueExposureLotsForSide /       |
//| StaleOldGridExposureLotsForSide) so each cached read equals the    |
//| value a fresh 0.0.39 scan would have produced at that program      |
//| point. NOT included (deliberately un-cached): CycleRealized        |
//| (deal-history, HistorySelect-order-sensitive) and                  |
//| CountLosingPositions (not on the hot path).                        |
//+------------------------------------------------------------------+
void RecomputeBookAggregates()
  {
   g_bookCache.myPositions        = 0;
   g_bookCache.myPendings         = 0;
   g_bookCache.rescueHedges       = 0;
   g_bookCache.trendEntries       = 0;
   g_bookCache.trendBuyEntries    = 0;
   g_bookCache.trendSellEntries   = 0;
   g_bookCache.avgEntries         = 0;
   g_bookCache.buyLots            = 0.0;
   g_bookCache.sellLots           = 0.0;
   g_bookCache.openLots           = 0.0;
   g_bookCache.avgTotalLots       = 0.0;
   g_bookCache.avgCoreBuyLots     = 0.0;
   g_bookCache.avgCoreSellLots    = 0.0;
   g_bookCache.trendBuySideLots   = 0.0;
   g_bookCache.trendSellSideLots  = 0.0;
   g_bookCache.staleBuyLots       = 0.0;
   g_bookCache.staleSellLots      = 0.0;
   g_bookCache.floating           = 0.0;
   g_bookCache.avgCoreBuyFloating = 0.0;
   g_bookCache.avgCoreSellFloating= 0.0;
   g_bookCache.floatEntries       = 0;
   g_bookCache.floatLots          = 0.0;
   g_bookCache.floatFloating      = 0.0;

   double rawFloating = 0.0;   // floating BEFORE the commission subtraction (matches OpenFloatingPL)

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);   // also selects the position
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;

      // CountMyPositions
      g_bookCache.myPositions++;

      double volume = PositionGetDouble(POSITION_VOLUME);
      double profitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      string comment = PositionGetString(POSITION_COMMENT);
      bool isRescueHedge = IsRescueHedgeComment(comment);
      bool isTrendRescue = IsTrendRescueComment(comment);
      bool isAveraging   = IsAveragingComment(comment);
      bool isFloat       = IsFloatTicket(ticket);

      // OpenFloatingPL (raw floating + openLots; commission subtracted after the loop)
      rawFloating += profitAndSwap;
      g_bookCache.openLots += volume;

      // Whole-book safety lanes include Float positions; cycle-scoped lanes
      // subtract only the explicit ticket registry below.
      if(isFloat)
        {
         g_bookCache.floatEntries++;
         g_bookCache.floatLots += volume;
         g_bookCache.floatFloating += profitAndSwap;
        }

      // NetExposureLots
      if(type == POSITION_TYPE_BUY)
         g_bookCache.buyLots += volume;
      else
         g_bookCache.sellLots += volume;

      // CountRescueHedges
      if(isRescueHedge)
         g_bookCache.rescueHedges++;

      // CountTrendRescueEntries / CountTrendRescueEntriesForDirection /
      // TrendRescueExposureLotsForSide
      if(isTrendRescue)
        {
         g_bookCache.trendEntries++;
         if(type == POSITION_TYPE_BUY)
           {
            g_bookCache.trendBuyEntries++;
            g_bookCache.trendBuySideLots += volume;
           }
         else
           {
            g_bookCache.trendSellEntries++;
            g_bookCache.trendSellSideLots += volume;
           }
        }

      // CountAveragingEntries / AveragingTotalLots
      if(isAveraging)
        {
         g_bookCache.avgEntries++;
         g_bookCache.avgTotalLots += volume;
        }

      // AveragingCoreExposureLots / AveragingCoreSideFloating
      // (grid + STR AV legs only; STR RHG hedge and STR TR legs excluded;
      //  0.0.42: STR OR orphan legs also excluded so the averaging core sees
      //  only fresh-cycle exposure)
      if(!isRescueHedge && !isTrendRescue && !isFloat)
        {
         if(type == POSITION_TYPE_BUY)
           {
            g_bookCache.avgCoreBuyLots += volume;
            g_bookCache.avgCoreBuyFloating += profitAndSwap;
           }
         else
           {
            g_bookCache.avgCoreSellLots += volume;
            g_bookCache.avgCoreSellFloating += profitAndSwap;
           }
        }

      // StaleOldGridExposureLotsForSide
      // (grid legs only; STR TR / STR RHG / STR AV excluded; stale-age AND losing;
      //  0.0.42: STR OR orphan legs also excluded)
      if(!isTrendRescue && !isRescueHedge && !isAveraging && !isFloat)
        {
         if(PositionAgeStale((datetime)PositionGetInteger(POSITION_TIME)) && profitAndSwap < 0.0)
           {
            if(type == POSITION_TYPE_BUY)
               g_bookCache.staleBuyLots += volume;
            else
               g_bookCache.staleSellLots += volume;
           }
        }
     }

   // OpenFloatingPL final step: subtract the per-lot close-side cost estimate.
   g_bookCache.floating = rawFloating - CommissionPerLotEffective() * g_bookCache.openLots;

   // TrendRescueExposureLotsForSide / StaleOldGridExposureLotsForSide normalize
   // the returned lots to 8 digits; mirror that here so the cached value matches.
   g_bookCache.trendBuySideLots  = NormalizeDouble(g_bookCache.trendBuySideLots, 8);
   g_bookCache.trendSellSideLots = NormalizeDouble(g_bookCache.trendSellSideLots, 8);
   g_bookCache.staleBuyLots      = NormalizeDouble(g_bookCache.staleBuyLots, 8);
   g_bookCache.staleSellLots     = NormalizeDouble(g_bookCache.staleSellLots, 8);

   // CountMyPendings: separate OrdersTotal() pass.
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);      // also selects the order
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;
      g_bookCache.myPendings++;
     }
  }

//+------------------------------------------------------------------+
//| 0.0.40 O1: refresh the aggregate cache once per dirty epoch.      |
//+------------------------------------------------------------------+
void EnsureBookAggregates()
  {
   if(g_bookDirty)
     {
      RecomputeBookAggregates();
      g_bookDirty = false;
     }
  }

//+------------------------------------------------------------------+
//| Count open positions belonging to this EA (magic + symbol)        |
//+------------------------------------------------------------------+
int CountMyPositions()
  {
   EnsureBookAggregates();
   return g_bookCache.myPositions;
  }

//+------------------------------------------------------------------+
//| Count losing open positions belonging to this EA (magic + symbol) |
//+------------------------------------------------------------------+
int CountLosingPositions()
  {
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap < 0.0)
         count++;
     }
   return count;
  }

//+------------------------------------------------------------------+
//| Count pending orders belonging to this EA (magic + symbol)        |
//+------------------------------------------------------------------+
int CountMyPendings()
  {
   EnsureBookAggregates();
   return g_bookCache.myPendings;
  }

bool IsRescueHedgeComment(const string comment)
  {
   return (StringFind(comment, "STR RHG") == 0);
  }

double OpenFloatingPL(double &openLots)
  {
   EnsureBookAggregates();
   openLots = g_bookCache.openLots;
   return g_bookCache.floating;
  }

//+------------------------------------------------------------------+
//| Float bucket accessors share the same atomic book snapshot.       |
//+------------------------------------------------------------------+
int CountFloatPositions()
  {
   EnsureBookAggregates();
   return g_bookCache.floatEntries;
  }

double FloatFloatingPL()
  {
   EnsureBookAggregates();
   return g_bookCache.floatFloating - CommissionPerLotEffective() * g_bookCache.floatLots;
  }

// Cycle-scoped floating excludes registered Float positions while whole-book
// safety lanes continue to include them.
double CycleFloatingPL(double &cycleLots)
  {
   EnsureBookAggregates();
   cycleLots = g_bookCache.openLots - g_bookCache.floatLots;
   return g_bookCache.floating - FloatFloatingPL();
  }

void NetExposureLots(double &buyLots, double &sellLots)
  {
   EnsureBookAggregates();
   buyLots = g_bookCache.buyLots;
   sellLots = g_bookCache.sellLots;
  }

int CountRescueHedges()
  {
   EnsureBookAggregates();
   return g_bookCache.rescueHedges;
  }

string TrendRescueDirectionName()
  {
   if(g_trendRescueDirection > 0)
      return "BUY";
   if(g_trendRescueDirection < 0)
      return "SELL";
   return "NONE";
  }

bool IsTrendRescueBuy()
  {
   return (g_trendRescueDirection > 0);
  }

string TrendRescueComment(const bool isBuy)
  {
   return (isBuy ? "STR TRB" : "STR TRS");
  }

bool IsTrendRescueComment(const string comment)
  {
   return (StringFind(comment, "STR TRB") == 0 ||
           StringFind(comment, "STR TRS") == 0);
  }

// 1.1.10 basket break-even recovery pendings (not grid STR B#/S#)
bool IsBasketRecoveryComment(const string comment)
  {
   return (StringFind(comment, "STR RCV") == 0);
  }

string BasketRecoveryComment(const bool isBuy)
  {
   return (isBuy ? "STR RCV B" : "STR RCV S");
  }

int CountTrendRescueEntries()
  {
   EnsureBookAggregates();
   return g_bookCache.trendEntries;
  }

int CountTrendRescueEntriesForDirection(const bool isBuy)
  {
   EnsureBookAggregates();
   return (isBuy ? g_bookCache.trendBuyEntries : g_bookCache.trendSellEntries);
  }

//+------------------------------------------------------------------+
//| 0.0.39 Basket Averaging comment tags / live-derived counters     |
//| Distinct STR AVB/AVS tags + dedicated counters keep the caps      |
//| independent of every other engine and make cleanup exclusion      |
//| unambiguous. Counters are recomputed from the LIVE book each tick |
//| (not stored) so a restart/recompile can never reset them.         |
//+------------------------------------------------------------------+
string AveragingComment(const bool isBuy)
  {
   return (isBuy ? "STR AVB" : "STR AVS");
  }

bool IsAveragingComment(const string comment)
  {
   return (StringFind(comment, "STR AVB") == 0 ||
           StringFind(comment, "STR AVS") == 0);
  }

//+------------------------------------------------------------------+
//| 0.0.44 FLOAT RE-ANCHOR registry. g_floatTickets[] is the single   |
//| source of per-tick membership truth for floated legs.             |
//| A floated leg KEEPS its "STR B#/S#" comment but is identified by   |
//| TICKET, so it claims no grid slot and is excluded from the fresh   |
//| cycle. Empty unless InpUseFloatReanchor => default-off no-op.      |
//+------------------------------------------------------------------+
bool IsFloatTicket(const ulong t)
  {
   for(int i = 0; i < g_floatCount; i++)
      if(g_floatTickets[i] == t)
         return true;
   return false;
  }

bool IsFloatPositionId(const ulong pid)
  {
   for(int i = 0; i < g_floatClosedCount; i++)
      if(g_floatClosedPositionIds[i] == pid)
         return true;
   return false;
  }

//+------------------------------------------------------------------+
//| Per-instance FLOAT registry persistence keys (mirror legacy release marker).     |
//| FRN = count of floated tickets; FRVar(i) = floated ticket[i].     |
//| FCN = count of float-closed position-ids; FCVar(i) = id[i].       |
//| ulong tickets/ids < 2^53 round-trip exactly through a double.     |
//+------------------------------------------------------------------+
string FRNVar()
  {
   return StringFormat("Straddle_FRN_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

string FRVar(const int i)
  {
   return StringFormat("Straddle_FR_%I64d_%I64d_%s_%d",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym, i);
  }

string FCNVar()
  {
   return StringFormat("Straddle_FCN_%I64d_%I64d_%s",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym);
  }

string FCVar(const int i)
  {
   return StringFormat("Straddle_FC_%I64d_%I64d_%s_%d",
                       (long)AccountInfoInteger(ACCOUNT_LOGIN), (long)InpMagic, g_sym, i);
  }

//+------------------------------------------------------------------+
//| Persist the whole float registry (tickets + closed position-ids). |
//| Writes the counts then each entry, and deletes any stale higher-  |
//| index GVs left over from a previously-larger registry. Called     |
//| after every mutation. ONLY ever reached under InpUseFloatReanchor.|
//+------------------------------------------------------------------+
void PersistFloatRegistry()
  {
   // Tickets
   GlobalVariableSet(FRNVar(), (double)g_floatCount);
   for(int i = 0; i < g_floatCount; i++)
      GlobalVariableSet(FRVar(i), (double)g_floatTickets[i]);
   // Prune any stale higher-index ticket GVs (registry shrank).
   for(int i = g_floatCount; i < g_floatCount + 64; i++)
     {
      if(GlobalVariableCheck(FRVar(i)))
         GlobalVariableDel(FRVar(i));
      else
         break;
     }
   // Closed position-ids
   GlobalVariableSet(FCNVar(), (double)g_floatClosedCount);
   for(int i = 0; i < g_floatClosedCount; i++)
      GlobalVariableSet(FCVar(i), (double)g_floatClosedPositionIds[i]);
   for(int i = g_floatClosedCount; i < g_floatClosedCount + 64; i++)
     {
      if(GlobalVariableCheck(FCVar(i)))
         GlobalVariableDel(FCVar(i));
      else
         break;
     }
  }

//+------------------------------------------------------------------+
//| Runtime registry mutators. Both persist after the change.         |
//+------------------------------------------------------------------+
void AddFloatTicket(const ulong t)
  {
   if(IsFloatTicket(t))
      return;
   ArrayResize(g_floatTickets, g_floatCount + 1);
   g_floatTickets[g_floatCount++] = t;
   PersistFloatRegistry();
  }

void RemoveFloatTicket(const ulong t)
  {
   for(int i = 0; i < g_floatCount; i++)
     {
      if(g_floatTickets[i] == t)
        {
         for(int j = i + 1; j < g_floatCount; j++)
            g_floatTickets[j - 1] = g_floatTickets[j];
         g_floatCount--;
         ArrayResize(g_floatTickets, g_floatCount);
         PersistFloatRegistry();
         return;
        }
     }
  }

void AddFloatClosedPositionId(const ulong pid)
  {
   if(IsFloatPositionId(pid))
      return;
   ArrayResize(g_floatClosedPositionIds, g_floatClosedCount + 1);
   g_floatClosedPositionIds[g_floatClosedCount++] = pid;
   PersistFloatRegistry();
  }

//+------------------------------------------------------------------+
//| Prune g_floatClosedPositionIds entries whose close (OUT) deal is  |
//| OLDER than `before` (the new cycle start). Such ids can no longer |
//| enter CycleRealized's [g_cycleStart, now] window, so dropping them |
//| keeps the set bounded without affecting the exclusion. Called on   |
//| each cycle start. Default-off no-op (set is empty).               |
//+------------------------------------------------------------------+
void PruneFloatClosedPositionIds(const datetime before)
  {
   if(g_floatClosedCount <= 0)
      return;
   bool changed = false;
   for(int i = g_floatClosedCount - 1; i >= 0; i--)
     {
      ulong pid = g_floatClosedPositionIds[i];
      bool keep = false;
      // Keep the id if its OUT deal is at/after `before` (still in a future window).
      if(HistorySelectByPosition(pid))
        {
         for(int d = HistoryDealsTotal() - 1; d >= 0; d--)
           {
            ulong deal = HistoryDealGetTicket(d);
            if(deal == 0)
               continue;
            if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal, DEAL_ENTRY) != DEAL_ENTRY_OUT)
               continue;
            datetime dt = (datetime)HistoryDealGetInteger(deal, DEAL_TIME);
            if(dt >= before)
              { keep = true; break; }
           }
        }
      if(keep)
         continue;
      // drop entry i (shift down)
      for(int j = i + 1; j < g_floatClosedCount; j++)
         g_floatClosedPositionIds[j - 1] = g_floatClosedPositionIds[j];
      g_floatClosedCount--;
      ArrayResize(g_floatClosedPositionIds, g_floatClosedCount);
      changed = true;
     }
   if(changed)
      PersistFloatRegistry();
  }

//+------------------------------------------------------------------+
//| Load the float registry on startup / cycle-recovery. For every    |
//| persisted ticket VALIDATE the position still exists; drop the     |
//| stale ones (self-healing), then rewrite the persisted set. Only   |
//| ever called under InpUseFloatReanchor => default-off no GV touch. |
//+------------------------------------------------------------------+
void LoadFloatRegistry()
  {
   g_floatCount = 0;
   ArrayResize(g_floatTickets, 0);
   g_floatClosedCount = 0;
   ArrayResize(g_floatClosedPositionIds, 0);

   bool changed = false;

   if(GlobalVariableCheck(FRNVar()))
     {
      int n = (int)GlobalVariableGet(FRNVar());
      for(int i = 0; i < n; i++)
        {
         if(!GlobalVariableCheck(FRVar(i)))
            continue;
         ulong t = (ulong)GlobalVariableGet(FRVar(i));
         if(t == 0)
           { changed = true; continue; }
         if(!PositionSelectByTicket(t))
           { changed = true; continue; }  // position gone => drop (self-heal)
         if(IsFloatTicket(t))
            continue;
         ArrayResize(g_floatTickets, g_floatCount + 1);
         g_floatTickets[g_floatCount++] = t;
        }
      if(g_floatCount != n)
         changed = true;
     }

   if(GlobalVariableCheck(FCNVar()))
     {
      int n = (int)GlobalVariableGet(FCNVar());
      for(int i = 0; i < n; i++)
        {
         if(!GlobalVariableCheck(FCVar(i)))
            continue;
         ulong pid = (ulong)GlobalVariableGet(FCVar(i));
         if(pid == 0)
           { changed = true; continue; }
         if(IsFloatPositionId(pid))
            continue;
         ArrayResize(g_floatClosedPositionIds, g_floatClosedCount + 1);
         g_floatClosedPositionIds[g_floatClosedCount++] = pid;
        }
      if(g_floatClosedCount != n)
         changed = true;
     }

   if(changed)
      PersistFloatRegistry();   // rewrite the validated/pruned set

   if(g_floatCount > 0 || g_floatClosedCount > 0)
      Log(1, StringFormat("Straddle: FLOAT loaded registry tickets=%d closedIds=%d",
                          g_floatCount, g_floatClosedCount));
  }

//+------------------------------------------------------------------+
//| Total floated-leg lots (for the InpMaxFloatLots cap). Scans the   |
//| registry against live positions.                                  |
//+------------------------------------------------------------------+
double CurrentFloatLots()
  {
   double lots = 0.0;
   for(int i = 0; i < g_floatCount; i++)
     {
      if(PositionSelectByTicket(g_floatTickets[i]))
         lots += PositionGetDouble(POSITION_VOLUME);
     }
   return lots;
  }

//+------------------------------------------------------------------+
//| 0.0.43 Staleness age of the FRESH-cycle book. Scans all          |
//| magic+symbol OPEN positions, SKIPS orphan-tagged (legacy comment classifier)|
//| and rescue-hedge (STR RHG) tickets, and returns                  |
//| (TimeCurrent() - earliest POSITION_TIME) in seconds over the      |
//| remaining fresh-cycle legs (0 if none). Deliberately does NOT     |
//| depend on g_cycleStart / g_cycleStartTrusted: a stale balanced    |
//| book may be untrusted, so this ages it directly from the broker's |
//| per-position open time. Used by the staleness-release branch to   |
//| free a net-flat balanced-residual cycle that never banks and      |
//| never trips the RED-floating orphan release.                     |
//+------------------------------------------------------------------+
double OldestCyclePositionAgeSeconds()
  {
   datetime earliest = 0;
   bool     found    = false;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      string comment = PositionGetString(POSITION_COMMENT);
      if(IsFloatTicket(ticket))
         continue;
      if(IsRescueHedgeComment(comment))
         continue;                       // STR RHG backstop hedge - not fresh-cycle
      datetime opened = (datetime)PositionGetInteger(POSITION_TIME);
      if(opened <= 0)
         continue;
      if(!found || opened < earliest)
        {
         earliest = opened;
         found    = true;
        }
     }
   if(!found)
      return 0.0;
   double age = (double)(TimeCurrent() - earliest);
   return (age > 0.0 ? age : 0.0);
  }

int CountAveragingEntries()
  {
   EnsureBookAggregates();
   return g_bookCache.avgEntries;
  }

double AveragingTotalLots()
  {
   EnsureBookAggregates();
   return g_bookCache.avgTotalLots;
  }

//+------------------------------------------------------------------+
//| 0.0.39 Buried-side exposure from GRID + STR AV legs ONLY.         |
//| Excludes STR RHG hedges and STR TR* legs so an open opposite-side |
//| hedge / trend leg can never misdirect or flip the buried side.    |
//+------------------------------------------------------------------+
void AveragingCoreExposureLots(double &buyLots, double &sellLots)
  {
   EnsureBookAggregates();
   buyLots = g_bookCache.avgCoreBuyLots;
   sellLots = g_bookCache.avgCoreSellLots;
  }

//+------------------------------------------------------------------+
//| 0.0.39 Floating P/L (profit+swap) on ONE side of the buried pile, |
//| counting GRID + STR AV legs only (hedge/trend excluded). Used as  |
//| the second confirmation of the buried-side trigger.              |
//+------------------------------------------------------------------+
double AveragingCoreSideFloating(const bool buriedIsBuy)
  {
   EnsureBookAggregates();
   return (buriedIsBuy ? g_bookCache.avgCoreBuyFloating : g_bookCache.avgCoreSellFloating);
  }

//+------------------------------------------------------------------+
//| 0.0.39 Worst open price on the buried side (grid + STR AV legs),  |
//| used as the adverse-step reference when g_basketAvgLastEntryPrice |
//| is unset (e.g. after restart). For buried longs the worst price   |
//| is the HIGHEST entry; for buried shorts the LOWEST entry.         |
//+------------------------------------------------------------------+
double AveragingCoreWorstOpenPrice(const bool buriedIsBuy)
  {
   ENUM_POSITION_TYPE wantedType = (buriedIsBuy ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);
   double worst = 0.0;
   bool found = false;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      string comment = PositionGetString(POSITION_COMMENT);
      if(IsRescueHedgeComment(comment) || IsTrendRescueComment(comment))
         continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != wantedType)
         continue;
      double price = PositionGetDouble(POSITION_PRICE_OPEN);
      if(!found)
        {
         worst = price;
         found = true;
        }
      else if(buriedIsBuy ? (price > worst) : (price < worst))
         worst = price;
     }
   return worst;
  }

void CollectTrendRescueSizingContext(const bool isBuy,
                                     double &losingSideLots,
                                     double &activeTrendLots)
  {
   losingSideLots = 0.0;
   activeTrendLots = 0.0;

   ENUM_POSITION_TYPE trendType = (isBuy ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);
   ENUM_POSITION_TYPE losingType = (isBuy ? POSITION_TYPE_SELL : POSITION_TYPE_BUY);

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;

      double volume = PositionGetDouble(POSITION_VOLUME);
      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      string comment = PositionGetString(POSITION_COMMENT);

      if(IsTrendRescueComment(comment))
        {
         if(type == trendType)
            activeTrendLots += volume;
         continue;
        }

      if(type == losingType)
         losingSideLots += volume;
     }
  }

double TrendRescueStep()
  {
   if(InpTrendRescueStepUSD > 0.0)
      return PriceDistanceInput(InpTrendRescueStepUSD);
   return GridStepDistance();
  }

void CaptureTrendRescueSnapshot(TrendRescueSnapshot &snapshot)
  {
   ZeroMemory(snapshot);

   snapshot.positions = CountMyPositions();
   snapshot.pendings = CountMyPendings();
   snapshot.trendEntries = CountTrendRescueEntries();
   snapshot.currentDirectionEntries = ((g_trendRescueDirection == 1 || g_trendRescueDirection == -1)
                                       ? CountTrendRescueEntriesForDirection(IsTrendRescueBuy())
                                       : 0);

   double openLots = 0.0;
   snapshot.floating = OpenFloatingPL(openLots);
   snapshot.cycleNet = CycleRealized() + snapshot.floating;

   double balanceDelta = 0.0;
   if(g_rescueAnchorTrusted && g_rescueAnchorBalance > 0.0)
      balanceDelta = AccountInfoDouble(ACCOUNT_BALANCE) - g_rescueAnchorBalance;

   snapshot.bookedProfit = MathMax(0.0, balanceDelta);
   snapshot.bookedLoss = MathMax(0.0, -balanceDelta);
   snapshot.floatingLoss = MathMax(0.0, -snapshot.floating);
   snapshot.required = snapshot.bookedLoss + snapshot.floatingLoss + MoneyInput(InpCleanupCostBufferUSD);
   snapshot.coverageGap = MathMax(0.0, snapshot.required - snapshot.bookedProfit);
   snapshot.effectiveHarvestTarget = TrendRescueEffectiveHarvestTarget(snapshot.coverageGap);
   snapshot.covered = (g_rescueAnchorTrusted && g_rescueAnchorBalance > 0.0 &&
                       snapshot.bookedProfit + 1.0e-6 >= snapshot.required);
   snapshot.valid = true;
   snapshot.stamp = TimeCurrent();
  }

void GetTrendRescueSnapshot(TrendRescueSnapshot &snapshot)
  {
   if(!g_trendRescueSnapshot.valid)
      CaptureTrendRescueSnapshot(g_trendRescueSnapshot);
   snapshot = g_trendRescueSnapshot;
  }

double TrendRescueCoverageGap()
  {
   TrendRescueSnapshot snapshot;
   GetTrendRescueSnapshot(snapshot);
   return snapshot.coverageGap;
  }

bool TrendRescueCoveragePressureActive(const double coverageGap)
  {
   if(!InpUseTrendRescueCoveragePressure)
      return false;
   if(coverageGap <= 0.0)
      return false;
   return (coverageGap >= MoneyInput(InpTrendRescuePressureGapUSD));
  }

double TrendRescuePressureTargetLots(const double losingSideLots,
                                     const double activeTrendLots,
                                     const int remainingSlots,
                                     const double maxEntryLot,
                                     const double coverageGap,
                                     bool &slotClipped)
  {
   slotClipped = false;
   double baseTarget = losingSideLots * MathMax(0.0, InpTrendRescueExposureRatio);
   if(!TrendRescueCoveragePressureActive(coverageGap))
      return baseTarget;

   double pressureRatio = MathMax(MathMax(0.0, InpTrendRescuePressureExposureRatio),
                                  MathMax(0.0, InpTrendRescueExposureRatio));
   double target = MathMax(baseTarget, losingSideLots * pressureRatio);

   double moneyGapLotStep = MoneyInput(InpTrendRescueMoneyGapLotStepUSD);
   if(moneyGapLotStep > 0.0 && maxEntryLot > 0.0)
     {
      double moneyGapTargetLots = (coverageGap / MoneyInput(InpTrendRescueMoneyGapLotStepUSD)) * maxEntryLot;
      target = MathMax(target, moneyGapTargetLots);
     }

   int extraEntries = InpTrendRescuePressureMinExtraEntries;
   if(extraEntries < 0)
      extraEntries = 0;
   if(extraEntries > remainingSlots)
      extraEntries = remainingSlots;
   if(extraEntries > 0 && maxEntryLot > 0.0)
      target = MathMax(target, activeTrendLots + maxEntryLot * (double)extraEntries);

   double reachableTarget = activeTrendLots;
   if(remainingSlots > 0 && maxEntryLot > 0.0)
      reachableTarget += maxEntryLot * (double)remainingSlots;
   if(reachableTarget > activeTrendLots && target > reachableTarget + 1.0e-8)
     {
      target = reachableTarget;
      slotClipped = true;
     }

   return target;
  }

double TrendRescueAdaptivePressure(const double coverageGap)
  {
   if(coverageGap <= 0.0)
      return 0.0;
   double scale = MathMax(MoneyInput(InpTrendRescueMaxAdaptiveHarvestUSD),
                          MoneyInput(InpTrendRescueMinAdaptiveHarvestUSD));
   if(scale <= 0.0)
      scale = MathMax(MoneyInput(InpTrendRescueProfitTargetUSD), MoneyInput(1.0));
   return MathMin(1.0, coverageGap / scale);
  }

int TrendRescueEffectiveCooldownSec(const double coverageGap)
  {
   int upper = InpTrendRescueCooldownSec;
   if(upper <= 0)
      return upper;
   if(!InpUseAdaptiveTrendRescueCadence)
     {
      double staticEquityDD = EquityFloatingDrawdown();
      double staticMultiplier = EquityPressureCooldownMultiplier(staticEquityDD);
      if(staticMultiplier > 1.0)
         return (int)MathRound((double)upper * staticMultiplier);
      return upper;
     }

   int lower = InpTrendRescueMinCooldownSec;
   if(lower < 0)
      lower = 0;
   if(lower > upper)
      lower = upper;

   double pressure = TrendRescueAdaptivePressure(coverageGap);
   int effective = (int)MathRound((double)upper - (double)(upper - lower) * pressure);
   if(effective < lower)
      effective = lower;
   if(effective > upper)
      effective = upper;
   double equityDD = EquityFloatingDrawdown();
   double multiplier = EquityPressureCooldownMultiplier(equityDD);
   if(multiplier > 1.0)
      effective = (int)MathRound((double)effective * multiplier);
   return effective;
  }

double TrendRescueEffectiveStep(const double coverageGap)
  {
   double upper = TrendRescueStep();
   if(upper <= 0.0)
      return NormalizeDouble(upper, g_digits);
   if(!InpUseAdaptiveTrendRescueCadence)
     {
      double staticEquityDD = EquityFloatingDrawdown();
      double staticMultiplier = EquityPressureStepMultiplier(staticEquityDD);
      double staticEffective = upper * staticMultiplier;
      double staticTick = TickSize();
      if(staticTick > 0.0 && staticEffective > 0.0)
         staticEffective = MathMax(staticTick, MathRound(staticEffective / staticTick) * staticTick);
      return NormalizeDouble(staticEffective, g_digits);
     }

   double lower = PriceDistanceInput(InpTrendRescueMinStepUSD);
   if(lower < 0.0)
      lower = 0.0;
   if(lower > upper)
      lower = upper;

   double pressure = TrendRescueAdaptivePressure(coverageGap);
   double effective = upper - (upper - lower) * pressure;
   double tick = TickSize();
   if(tick > 0.0 && effective > 0.0)
      effective = MathMax(tick, MathRound(effective / tick) * tick);
   if(effective < lower)
      effective = lower;
   if(effective > upper)
      effective = upper;
   double equityDD = EquityFloatingDrawdown();
   double multiplier = EquityPressureStepMultiplier(equityDD);
   if(multiplier > 1.0)
     {
      effective *= multiplier;
      if(tick > 0.0 && effective > 0.0)
         effective = MathMax(tick, MathRound(effective / tick) * tick);
     }
   return NormalizeDouble(effective, g_digits);
  }

int TrendRescueEntrySkipLogLevel(const double coverageGap)
  {
   if(coverageGap > MoneyInput(InpTrendRescueSkipDiagGapUSD))
      return 1;
   return 2;
  }

bool TrendRescueSkipLogAllowed(const string reason, const double coverageGap)
  {
   if(!TrendRescueHotSkipReason(reason))
      return true;
   int throttle = InpTrendRescueSkipLogThrottleSec;
   if(TesterLowLogActive() && InpSuppressRepeatedSkipLogs)
      throttle = TesterLowLogSummarySec();
   if(throttle <= 0)
      return true;

   datetime now = TimeCurrent();
   double bucket = TrendRescueSkipCoverageBucket(coverageGap);
   string lowLogKey = StringFormat("trend-skip|%s|bucket=%s",
                                   reason, DoubleToString(bucket, 0));
   if(TesterLowLogActive() && InpSuppressRepeatedSkipLogs && InpLogStateChangeOnly &&
      !ShouldEmitLog(TrendRescueEntrySkipLogLevel(coverageGap), lowLogKey, lowLogKey))
      return false;
   if(g_lastTrendRescueSkipReason == reason &&
      MathAbs(g_lastTrendRescueSkipBucket - bucket) < 1.0e-8 &&
      g_lastTrendRescueSkipLog > 0 &&
      (now - g_lastTrendRescueSkipLog) < throttle)
      return false;
   g_lastTrendRescueSkipReason = reason;
   g_lastTrendRescueSkipBucket = bucket;
   g_lastTrendRescueSkipLog = now;
   return true;
  }

void LogTrendRescueEntrySkip(const string reason,
                             const string msg,
                             const double coverageGap)
  {
   if(!TrendRescueSkipLogAllowed(reason, coverageGap))
      return;
   Log(TrendRescueEntrySkipLogLevel(coverageGap), msg);
  }

bool ShouldPrepareTrendRescueEntrySkipLog(const string reason,
                                          const double coverageGap)
  {
   return TrendRescueSkipLogAllowed(reason, coverageGap);
  }

void EmitTrendRescueEntrySkipPrepared(const string reason,
                                      const string msg,
                                      const double coverageGap)
  {
   Log(TrendRescueEntrySkipLogLevel(coverageGap), msg);
  }

double TrendRescueEffectiveHarvestTarget(const double coverageGap)
  {
   if(MoneyInput(InpTrendRescueProfitTargetUSD) <= 0.0)
      return 0.0;
   if(!InpUseAdaptiveTrendRescueHarvest || coverageGap <= 0.0)
      return MoneyInput(InpTrendRescueProfitTargetUSD);

   double adaptiveTarget = coverageGap * InpTrendRescueHarvestGapShare;
   adaptiveTarget = MathMax(adaptiveTarget, MoneyInput(InpTrendRescueMinAdaptiveHarvestUSD));
   if(MoneyInput(InpTrendRescueMaxAdaptiveHarvestUSD) > 0.0)
      adaptiveTarget = MathMin(adaptiveTarget, MoneyInput(InpTrendRescueMaxAdaptiveHarvestUSD));

   return MathMax(MoneyInput(InpTrendRescueProfitTargetUSD), adaptiveTarget);
  }

double NormalizeTrendRescueEntryLot(const double requestedLots,
                                    const double maxEntryLot,
                                    const bool allowRoundUpToMin)
  {
   if(requestedLots <= 0.0 || maxEntryLot <= 0.0)
      return 0.0;

   double vmin = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   double vmax = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MAX);
   double vstep = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP);
   double maxAllowed = maxEntryLot;
   if(vmax > 0.0 && maxAllowed > vmax)
      maxAllowed = vmax;
   if(vmin > 0.0 && maxAllowed + 1.0e-8 < vmin)
      return 0.0;

   double lots = requestedLots;
   if(lots > maxAllowed)
      lots = maxAllowed;
   if(allowRoundUpToMin && vmin > 0.0 && lots < vmin)
      lots = vmin;

   if(vstep > 0.0)
     {
      if(allowRoundUpToMin)
         lots = MathRound(lots / vstep) * vstep;
      else
         lots = MathFloor((lots + 1.0e-12) / vstep) * vstep;

      if(lots > maxAllowed + 1.0e-8)
         lots = MathFloor((maxAllowed + 1.0e-12) / vstep) * vstep;
      if(allowRoundUpToMin && vmin > 0.0 && lots + 1.0e-8 < vmin)
         lots = MathCeil((vmin - 1.0e-12) / vstep) * vstep;
     }

   lots = NormalizeDouble(lots, 8);
   if(lots <= 0.0)
      return 0.0;
   if(vmin > 0.0 && lots + 1.0e-8 < vmin)
      return 0.0;
   if(lots > maxAllowed + 1.0e-8)
      return 0.0;
   return lots;
  }

//+------------------------------------------------------------------+
//| Leg comment: side + level encoded so every leg is identifiable    |
//| ("STR B5" = BUY level 5, "STR S5" = SELL level 5). Used for slot  |
//| occupancy matching (population rule) and restart recovery.        |
//+------------------------------------------------------------------+
string LegComment(const bool isBuy, const int level)
  {
   return StringFormat("STR %s%d", (isBuy ? "B" : "S"), level);
  }

bool IsStraddleComment(const string comment)
  {
   return (StringFind(comment, "STR ") == 0);
  }

//+------------------------------------------------------------------+
//| Parse a leg comment back into (side, level). Tolerates a broker-  |
//| appended suffix after the digits (cuts at the first non-digit) so |
//| servers that decorate comments do not orphan slots. Returns false |
//| for foreign/empty/altered-beyond-recognition comments.            |
//+------------------------------------------------------------------+
bool ParseLegComment(const string comment, bool &isBuy, int &level)
  {
   if(StringLen(comment) < 6)
      return false;
   if(StringSubstr(comment, 0, 4) != "STR ")
      return false;
   ushort side = StringGetCharacter(comment, 4);
   if(side == 'B')
      isBuy = true;
   else if(side == 'S')
      isBuy = false;
   else
      return false;
   string rest = StringSubstr(comment, 5);
   int len = StringLen(rest), nd = 0;
   for(int i = 0; i < len; i++)
     {
      ushort c = StringGetCharacter(rest, i);
      if(c >= '0' && c <= '9')
         nd++;
      else
         break;
     }
   if(nd == 0)
      return false;
   level = (int)StringToInteger(StringSubstr(rest, 0, nd));
   return (level >= 1);
  }

//+------------------------------------------------------------------+
//| FIXED price of grid level k for a side (0.0.8). The anchor is set |
//| ONCE at cycle start and never re-centered within a cycle.         |
//+------------------------------------------------------------------+
double LevelPrice(const bool isBuy, const int level)
  {
   return SnapPrice(g_anchor + (isBuy ? 1.0 : -1.0) * level * CycleGridStepDistance());
  }

bool IsPendingPlacementSuccessRetcode(const uint rc)
  {
   return (rc == TRADE_RETCODE_PLACED || rc == TRADE_RETCODE_DONE);
  }

double MarketValidationMinMarginLevelFloorPct()
  {
   double floor = 0.0;
   if(InpMarketValidationMinMarginLevelAfterCheckPct > 0.0)
      floor = InpMarketValidationMinMarginLevelAfterCheckPct;

   long stopoutMode = AccountInfoInteger(ACCOUNT_MARGIN_SO_MODE);
   if(stopoutMode == ACCOUNT_STOPOUT_MODE_PERCENT)
     {
      double stopoutLevel = AccountInfoDouble(ACCOUNT_MARGIN_SO_SO);
      if(stopoutLevel > 0.0)
         floor = MathMax(floor, stopoutLevel);
     }

   return floor;
  }

bool MarketValidationProjectedFreeMarginAboveStopoutMoney(const double projectedFreeMargin,
                                                          const string context)
  {
   long stopoutMode = AccountInfoInteger(ACCOUNT_MARGIN_SO_MODE);
   if(stopoutMode != ACCOUNT_STOPOUT_MODE_MONEY)
      return true;

   double stopoutMoney = AccountInfoDouble(ACCOUNT_MARGIN_SO_SO);
   if(stopoutMoney <= 0.0)
      return true;
   if(projectedFreeMargin > stopoutMoney)
      return true;

   Log(2, StringFormat("Straddle: %s skipped - projected free margin %.2f <= money stopout %.2f",
                       context, projectedFreeMargin, stopoutMoney));
   return false;
  }

bool MarketValidationSparseOrderCheckCommentOk(const string comment)
  {
   string text = LowerCopy(comment);
   if(StringLen(text) == 0)
      return true;

   if(text == "done" || text == "done." ||
      text == "ok" || text == "ok." ||
      text == "request completed")
      return true;

   if(StringFind(text, "market closed") >= 0 ||
      StringFind(text, "trade disabled") >= 0 ||
      StringFind(text, "trading disabled") >= 0 ||
      StringFind(text, "session closed") >= 0 ||
      StringFind(text, "quotes disabled") >= 0 ||
      StringFind(text, "price off") >= 0 ||
      StringFind(text, "off quotes") >= 0 ||
      StringFind(text, "not enough money") >= 0 ||
      StringFind(text, "no money") >= 0 ||
      StringFind(text, "insufficient margin") >= 0 ||
      StringFind(text, "stop out") >= 0 ||
      StringFind(text, "stopout") >= 0 ||
      StringFind(text, "invalid stops") >= 0 ||
      StringFind(text, "invalid volume") >= 0 ||
      StringFind(text, "invalid price") >= 0 ||
      StringFind(text, "invalid order") >= 0 ||
      StringFind(text, "frozen") >= 0 ||
      StringFind(text, "limit orders") >= 0 ||
      StringFind(text, "limit volume") >= 0 ||
      StringFind(text, "limit positions") >= 0)
      return false;

   return true;
  }

void MarketValidationApplySparseOrderCheckCommentAction(const string comment)
  {
   string text = LowerCopy(comment);
   if(StringFind(text, "market closed") >= 0 ||
      StringFind(text, "trade disabled") >= 0 ||
      StringFind(text, "trading disabled") >= 0 ||
      StringFind(text, "session closed") >= 0 ||
      StringFind(text, "quotes disabled") >= 0 ||
      StringFind(text, "price off") >= 0 ||
      StringFind(text, "off quotes") >= 0 ||
      StringFind(text, "frozen") >= 0)
     {
      g_buildSkipWait = true;
      return;
     }

   if(StringFind(text, "not enough money") >= 0 ||
      StringFind(text, "no money") >= 0 ||
      StringFind(text, "insufficient margin") >= 0 ||
      StringFind(text, "stop out") >= 0 ||
      StringFind(text, "stopout") >= 0 ||
      StringFind(text, "limit orders") >= 0 ||
      StringFind(text, "limit volume") >= 0 ||
      StringFind(text, "limit positions") >= 0)
      g_abortBuild = true;
  }

bool MarketValidationFallbackBadRetcode(const uint retcode)
  {
   switch(retcode)
     {
      case TRADE_RETCODE_INVALID:
      case TRADE_RETCODE_INVALID_VOLUME:
      case TRADE_RETCODE_INVALID_PRICE:
      case TRADE_RETCODE_INVALID_STOPS:
      case TRADE_RETCODE_TRADE_DISABLED:
      case TRADE_RETCODE_MARKET_CLOSED:
      case TRADE_RETCODE_NO_MONEY:
      case TRADE_RETCODE_SERVER_DISABLES_AT:
      case TRADE_RETCODE_CLIENT_DISABLES_AT:
      case TRADE_RETCODE_LOCKED:
      case TRADE_RETCODE_FROZEN:
      case TRADE_RETCODE_INVALID_FILL:
      case TRADE_RETCODE_ONLY_REAL:
      case TRADE_RETCODE_LIMIT_ORDERS:
      case TRADE_RETCODE_LIMIT_VOLUME:
      case TRADE_RETCODE_INVALID_ORDER:
      case TRADE_RETCODE_LIMIT_POSITIONS:
      case TRADE_RETCODE_LONG_ONLY:
      case TRADE_RETCODE_SHORT_ONLY:
      case TRADE_RETCODE_CLOSE_ONLY:
      case TRADE_RETCODE_FIFO_CLOSE:
      case TRADE_RETCODE_HEDGE_PROHIBITED:
         return true;
      default:
         return false;
     }
  }

bool MarketValidationFallbackEligibleAfterZeroGrid(const int placed)
  {
   if(placed != 0)
      return false;
   if(!InpUseMarketValidationSafety)
      return false;
   if(!g_marketValidationMinLotInflationSkip)
      return false;
   if(g_marketValidationOtherZeroPlacementCause)
      return false;
   if(g_abortBuild || g_buildSkipWait || IsStopped())
      return false;
   return true;
  }

bool TryMarketValidationFallbackTrade()
  {
   if(IsStopped() || !InpUseMarketValidationSafety || !g_marketValidationMinLotInflationSkip)
      return false;
   if(g_marketValidationOtherZeroPlacementCause || g_abortBuild || g_buildSkipWait)
      return false;
   if(CountMyPositions() > 0 || CountMyPendings() > 0)
      return false;
   if(!MarketValidationFinalSendAllowed("market-validation fallback"))
     {
      g_buildSkipWait = true;
      return false;
     }

   double bid = 0.0, ask = 0.0;
   if(!RefreshCurrentPrices(bid, ask))
      return false;

   double lots = NormalizeLot(SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN));
   if(lots <= 0.0)
      return false;

   double point = SymbolInfoDouble(g_sym, SYMBOL_POINT);
   double tick = TickSize();
   long stopsLevel = SymbolInfoInteger(g_sym, SYMBOL_TRADE_STOPS_LEVEL);
   long freezeLevel = SymbolInfoInteger(g_sym, SYMBOL_TRADE_FREEZE_LEVEL);
   long minLevel = (stopsLevel > freezeLevel ? stopsLevel : freezeLevel);
   double minDistance = (double)minLevel * point + tick;
   if(point <= 0.0 || tick <= 0.0 || minDistance <= 0.0)
      return false;

   double price = NormalizeDouble(ask, g_digits);
   double sl = NormalizeDouble(SnapDown(bid - minDistance), g_digits);
   double tp = NormalizeDouble(SnapUp(ask + minDistance), g_digits);
   if(sl <= 0.0 || tp <= 0.0)
      return false;
   if(sl >= bid || tp <= ask)
      return false;
   if((bid - sl) + tick * 0.5 < minDistance ||
      (tp - ask) + tick * 0.5 < minDistance)
      return false;

   double requiredMargin = 0.0;
   if(!OrderCalcMargin(ORDER_TYPE_BUY, g_sym, lots, price, requiredMargin) ||
      requiredMargin <= 0.0)
      return false;

   double minFreeMargin = MoneyInput(InpMarketValidationMinFreeMarginAfterCheckUSD);
   double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
   double projectedFreeMargin = freeMargin - requiredMargin;
   if(minFreeMargin > 0.0 && freeMargin <= requiredMargin + minFreeMargin)
     {
      Log(2, StringFormat("Straddle: market-validation fallback skipped - projected free margin %.2f below %.2f after required margin %.2f",
                          projectedFreeMargin, minFreeMargin, requiredMargin));
      return false;
     }
   if(!MarketValidationProjectedFreeMarginAboveStopoutMoney(projectedFreeMargin, "market-validation fallback"))
     {
      g_abortBuild = true;
      return false;
     }

   double minMarginLevel = MarketValidationMinMarginLevelFloorPct();
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   double projectedMargin = AccountInfoDouble(ACCOUNT_MARGIN) + requiredMargin;
   if(minMarginLevel > 0.0 && equity > 0.0 && projectedMargin > 0.0)
     {
      double projectedMarginLevel = equity / projectedMargin * 100.0;
      if(projectedMarginLevel < minMarginLevel)
        {
         Log(2, StringFormat("Straddle: market-validation fallback skipped - projected margin level %.2f%% below %.2f%%",
                             projectedMarginLevel, minMarginLevel));
         return false;
        }
     }

   StrActionOutcome outcome=ExecuteMarketDeal(ORDER_TYPE_BUY,lots,sl,tp,"STR MVF",
                                              minMarginLevel,minFreeMargin);
   uint rc=outcome.retcode;
   if(TradeOutcomeAccepted(outcome))
     {
      g_bookDirty = true;
      LogAlways(StringFormat("Straddle: market-validation fallback opened BUY %.2f at %s with SL %s TP %s after zero grid orders from min-lot inflation",
                             lots,
                             DoubleToString((outcome.result_price>0.0 ? outcome.result_price : price), g_digits),
                             DoubleToString(sl, g_digits),
                             DoubleToString(tp, g_digits)));
      return true;
     }

   ENUM_RETCODE_ACTION action = ClassifyRetcode(rc);
   if(action == ACT_ABORT_CYCLE)
      g_abortBuild = true;
   else if(action == ACT_SKIP_WAIT)
      g_buildSkipWait = true;
   else if(action == ACT_BACKOFF)
      g_nextBuildTry = TimeCurrent() + InpRetrySeconds;
   else if(action != ACT_RETRY_REFRESH)
      LogOp(StringFormat("Straddle: market-validation fallback failed, retcode %u (%s)",
                         rc, outcome.result_comment));
   return false;
  }

bool ValidatePlacedStopOrder(const ulong ticket,
                             const bool isBuy,
                             const int level,
                             const string expectedComment,
                             const double expectedPrice)
  {
   if(ticket == 0)
      return false;
   if(!OrderSelect(ticket))
      return false;
   if(OrderGetString(ORDER_SYMBOL) != g_sym)
      return false;
   if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
      return false;

   ENUM_ORDER_TYPE expectedType = (isBuy ? ORDER_TYPE_BUY_STOP : ORDER_TYPE_SELL_STOP);
   if((ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE) != expectedType)
      return false;
   if(OrderGetString(ORDER_COMMENT) != expectedComment)
      return false;

   double actualPrice = NormalizeDouble(OrderGetDouble(ORDER_PRICE_OPEN), g_digits);
   double fixedPrice  = NormalizeDouble(expectedPrice, g_digits);
   return SameTickPrice(actualPrice, fixedPrice);
  }

int EffectiveGridLevels()
  {
   int lv = InpGridLevels;
   if(g_lowPricedSymbol && lv > LOWPRICE_MAX_LEVELS)
      lv = LOWPRICE_MAX_LEVELS;
   // 1.0.6: never request more simultaneous pendings than the account allows.
   // ACCOUNT_LIMIT_ORDERS==0 means unlimited -> NO cap (real brokers: gold unchanged).
   // The MQL5 validator emulates a non-zero limit; a 2-sided grid of 2*lv pendings would
   // otherwise trip TRADE_RETCODE_LIMIT_ORDERS (10033) and fail validation.
   long lim = AccountInfoInteger(ACCOUNT_LIMIT_ORDERS);
   if(lim > 0)
     {
      int maxByLimit = (int)((lim - ORDER_LIMIT_SAFETY_BUFFER) / 2);
      if(maxByLimit < 1)
         maxByLimit = 1;
      if(lv > maxByLimit)
         lv = maxByLimit;
     }
   return lv;
  }

double GridMinPrice()
  {
   return LevelPrice(false, EffectiveGridLevels());
  }

double GridMaxPrice()
  {
   return LevelPrice(true, EffectiveGridLevels());
  }

void ResetCycleState()
  {
   g_anchor     = 0.0;
   ClearPersistedCycleStart();
   ClearPersistedCycleGridStepDistance();
   ArrayResize(g_legs, 0);
   // 1.1.14: new cycle window — clear TREND-2X recovery latch
   g_trend2xArmedThisCycle = false;
   g_trend2xWasActive = false;
   g_trend2xLastDir = 0;
  }

//+------------------------------------------------------------------+
//| 1.1.18: server-day 00:00 stamp                                    |
//+------------------------------------------------------------------+
datetime DailyDayStamp(const datetime t)
  {
   MqlDateTime dt;
   TimeToStruct(t, dt);
   dt.hour = 0;
   dt.min  = 0;
   dt.sec  = 0;
   return StructToTime(dt);
  }

//+------------------------------------------------------------------+
//| 1.1.20: day EQUITY P/L = current equity - day-start equity.       |
//| Includes floating; tracks the green equity line on the chart.     |
//+------------------------------------------------------------------+
double CalcDailyEquityPL()
  {
   if(g_dayStartEquity <= 0.0)
      return 0.0;
   return AccountInfoDouble(ACCOUNT_EQUITY) - g_dayStartEquity;
  }

// 1.1.23: a daily stop is stronger than the generic no-loss teardown. If a
// close was interrupted by rollover, restart, or a closed market, restore the
// explicit force-close mode while matching exposure still exists. The marker
// is cleared only after ProcessTearDown confirms the book is truly flat.
bool RestorePersistedDailyStopIfNeeded()
  {
   if(!HasPersistedDailyStopMarker())
      return false;

   const int positions = CountMyPositions();
   const int pendings  = CountMyPendings();
   if(positions <= 0 && pendings <= 0)
     {
      ClearPersistedDailyStopMarker();
      return false;
     }

   const bool alreadyActive = (g_dailyTradingStopped &&
                               g_dailyStopForceClose &&
                               g_tearingDown);
   if(alreadyActive)
      return true;

   g_dailyTradingStopped = true;
   g_dailyStopForceClose = true;
   g_dailyStopReason = "resuming persisted daily equity stop until flat";
   g_basketTearDown = false;
   ClearPersistedBasketTearDownTag();
   g_rescueHolding = false;
   GlobalVariableDel(RHVar());
   ClearRescueAnchorBalance();
   ClearRescueHedgeTime();
   ClearTrendRescueState(false);
   g_tearingDown = true;
   g_nextTearTry = 0;
   g_teardownSafeThreshold = 0.0;
   if(!GlobalVariableCheck(TDVar()))
     {
      if(!GlobalVariableSet(TDVar(), (double)TimeCurrent()))
         Log(0, "Straddle: WARN could not restore daily-stop teardown marker");
     }
   PersistTearDownThreshold();

   LogAlways(StringFormat(
      "Straddle: RESUMING PERSISTED DAILY STOP - force-close pos=%d pend=%d until flat",
      positions, pendings));
   return true;
  }

//+------------------------------------------------------------------+
//| 1.1.23: roll day, equity day P/L, daily stop, next-day lot x2.    |
//+------------------------------------------------------------------+
void UpdateDailyTradingLimits()
  {
   const datetime day0 = DailyDayStamp(TimeCurrent());

   if(!InpUseDailyLimits && !InpUseOpenBookHardStop && !g_tearingDown)
     {
      // Both protections are disabled. Do not interrupt a force-close already
      // in progress; it must still finish safely.
      // 1.1.35: still honor an active multi-day no-trade cooldown from a prior loss.
      if(ApplyLossCooldownForDay(day0, false))
        {
         RestorePersistedDailyStopIfNeeded();
         g_dailyEquityPL = CalcDailyEquityPL();
         return;
        }
      g_dailyTradingStopped = false;
      g_dailyStopForceClose = false;
      g_dailyStopReason = "";
      if(HasPersistedDailyStopMarker() &&
         (CountMyPositions() + CountMyPendings() <= 0))
         ClearPersistedDailyStopMarker();
      return;
     }

   // First call / mid-day restart: restore equity anchor for this server day
   if(g_dailyDayStamp == 0)
     {
      LoadOrInitDayStartEquity(day0);
      // 1.1.35: restore multi-day loss cooldown before allowing a mid-day restart trade.
      if(!ApplyLossCooldownForDay(day0, true))
        {
         g_dailyTradingStopped = false;
         g_dailyStopReason = "";
        }
     }

   if(day0 != g_dailyDayStamp)
     {
      const bool hadPrior = (g_dailyDayStamp != 0);
      const bool wasStopped = g_dailyTradingStopped;
      const bool endingRecoveryDay = g_recoveryLot2xToday;
      const bool pendingPriorDailyStop =
         HasPersistedDailyStopMarker() &&
         (CountMyPositions() + CountMyPendings() > 0);

      // End of recovery day: drop 2x lots
      if(endingRecoveryDay)
        {
         g_recoveryLot2xToday = false;
         LogAlways("Straddle: RECOVERY LOT x2 day ended - lots and target back to 1x");
        }

      // 1.1.35: know cooldown state BEFORE consuming next-day lot2x so recovery
      // x2 arms on the first free trade day after a multi-day no-trade pause.
      LoadLossCooldown();
      const bool stillInLossCooldown = IsLossCooldownActive(day0);

      // Consume pending flag from a prior equity-loss-limit day
      // only when trading may resume today (not still in multi-day pause).
      LoadPendingNextDayLot2x();
      if(!stillInLossCooldown && InpDailyLossNextDayLot2x && g_pendingNextDayLot2x)
        {
         g_recoveryLot2xToday = true;
         g_pendingNextDayLot2x = false;
         PersistPendingNextDayLot2x(false);
         LogAlways(StringFormat(
             "Straddle: RECOVERY x2 ACTIVE today - lots %.2f/%.2f/%.2f | cycle target %.1f | day profit lim %.1f | day loss lim HARD %.1f",
             (IsAutoLotMode() ? InpAutoLotNear * AutoLotScaleFactor() : InpLotNear) * 2.0,
             (IsAutoLotMode() ? InpAutoLotMid  * AutoLotScaleFactor() : InpLotMid)  * 2.0,
             (IsAutoLotMode() ? InpAutoLotFar  * AutoLotScaleFactor() : InpLotFar)  * 2.0,
             EffectiveCycleTargetUSD(),
             EffectiveDailyProfitLimitUSD(),
              EffectiveDailyLossLimitUSD()));
        }
      else
        {
         g_recoveryLot2xToday = false;
         if(stillInLossCooldown && g_pendingNextDayLot2x)
            LogAlways("Straddle: RECOVERY x2 pending deferred - still in daily-loss no-trade cooldown");
        }

      // New day equity anchor = current equity (floating starts fresh for the day)
      g_dailyDayStamp  = day0;
      g_dayStartEquity = AccountInfoDouble(ACCOUNT_EQUITY);
      PersistDayStartEquity();
      g_dailyEquityPL = 0.0;

      // 1.1.35: multi-day loss cooldown may keep trading stopped across midnight.
      const bool cooldownBlocks = ApplyLossCooldownForDay(day0, true);
      if(!cooldownBlocks)
        {
         g_dailyTradingStopped = false;
         g_dailyStopForceClose = false;
         g_dailyStopReason = "";
        }

      if(hadPrior && wasStopped && !pendingPriorDailyStop && !cooldownBlocks)
         LogAlways(StringFormat(
            "Straddle: DAILY LIMITS - new day RESUMED (equity anchor=%.2f)", g_dayStartEquity));
      else if(hadPrior && wasStopped && pendingPriorDailyStop)
         LogAlways(StringFormat(
            "Straddle: DAILY LIMITS - new day started but prior stop remains force-closing residual exposure (equity anchor=%.2f)",
            g_dayStartEquity));
      else if(hadPrior && cooldownBlocks)
         LogAlways(StringFormat(
            "Straddle: DAILY LIMITS - new day still in loss no-trade cooldown (equity anchor=%.2f)",
            g_dayStartEquity));
      else if(hadPrior)
         LogAlways(StringFormat(
            "Straddle: DAILY LIMITS - new day equity anchor=%.2f", g_dayStartEquity));
      }

   // Keep a prior daily force-close active across midnight/restart until the
   // matching book is actually flat. A fresh day must not release residual
   // positions into the generic rescue-hold path.
   RestorePersistedDailyStopIfNeeded();
   // 1.1.35: re-assert multi-day cooldown each tick while active (restart-safe).
   if(!g_dailyTradingStopped)
      ApplyLossCooldownForDay(day0, false);
   g_dailyEquityPL = CalcDailyEquityPL();
   if(g_dailyTradingStopped)
     {
      // 1.1.23: if stop is armed but residual positions/pendings remain (float skip,
      // partial close, restart), re-drive force-close until truly flat.
      if(!g_tearingDown && (CountMyPositions() + CountMyPendings()) > 0)
        {
         LogAlways(StringFormat(
            "Straddle: DAILY STOP re-arm - residual exposure pos=%d pend=%d dayPL=%.2f",
            CountMyPositions(), CountMyPendings(), g_dailyEquityPL));
         BeginDailyStopAndClose(
            (g_dailyStopReason != "" ? g_dailyStopReason : "daily stop residual flatten"));
        }
      return;
     }

    // Recovery day: lots and cycle target are x2. Daily equity stop caps are
    // not doubled by recovery; fixed mode uses dollar inputs and auto mode
    // uses the configured percentages of the day-start equity basis.
   const double profitLim = EffectiveDailyProfitLimitUSD();
   const double lossLim   = EffectiveDailyLossLimitUSD();
   const double eq = AccountInfoDouble(ACCOUNT_EQUITY);
   double openBookLots = 0.0;
   const double openBookPL = (InpUseOpenBookHardStop
                              ? OpenFloatingPL(openBookLots)
                              : 0.0);
   const bool profitByDailyEquity = (profitLim > 0.0 &&
                                     g_dailyEquityPL + 1.0e-6 >= profitLim);
   const bool profitByOpenBook = (InpUseOpenBookHardStop && profitLim > 0.0 &&
                                  openBookPL + 1.0e-6 >= profitLim);
   const bool lossByDailyEquity = (lossLim > 0.0 &&
                                   g_dailyEquityPL - 1.0e-6 <= -lossLim);
   const bool lossByOpenBook = (InpUseOpenBookHardStop && lossLim > 0.0 &&
                                openBookPL - 1.0e-6 <= -lossLim);

   // Day equity P/L = equity - dayStartEquity (floating included).
   // Strict open-book guard: this EA's live floating P/L is also a hard
   // trigger, so a visible -$700 book cannot remain open merely because
   // realized P/L or another account position offsets account equity.
   // Profit/loss from either source stops the day.
   if(profitByDailyEquity || profitByOpenBook)
     {
      const string basis = (profitByDailyEquity && profitByOpenBook
                            ? "daily-equity+open-book"
                            : (profitByDailyEquity ? "daily-equity" : "open-book"));
      g_dailyStopReason = StringFormat("strict profit trigger=%s dailyPL=+%.2f openBook=+%.2f >= +%.2f%s (eq=%.2f start=%.2f)",
                                       basis, g_dailyEquityPL, openBookPL, profitLim,
                                       (IsRecoveryLot2xToday() ? " [2x lots]" : ""),
                                       eq, g_dayStartEquity);
      LogAlways(StringFormat(
         "Straddle: DAILY EQUITY PROFIT LIMIT - %s - STOP AND CLOSE (flat rest of day)",
         g_dailyStopReason));
      BeginDailyStopAndClose(g_dailyStopReason);
      return;
      }
   if(lossByDailyEquity || lossByOpenBook)
     {
      const string basis = (lossByDailyEquity && lossByOpenBook
                            ? "daily-equity+open-book"
                            : (lossByDailyEquity ? "daily-equity" : "open-book"));
      g_dailyStopReason = StringFormat("strict loss trigger=%s dailyPL=%.2f openBook=%.2f <= -%.2f%s (eq=%.2f start=%.2f)",
                                       basis, g_dailyEquityPL, openBookPL, lossLim,
                                       (IsRecoveryLot2xToday() ? " [2x lots]" : ""),
                                       eq, g_dayStartEquity);
      // 1.1.36: multi-day no-trade only when the account DAY EQUITY loss cap
      // is hit. Open-book hard stop alone still force-closes for the rest of
      // THIS day (1.1.34 parity) but does not freeze the next N days when
      // day equity is still above the loss limit (common green-day float DD).
      if(lossByDailyEquity)
        {
         LogAlways(StringFormat(
            "Straddle: DAILY EQUITY LOSS LIMIT - %s - STOP AND CLOSE (no-trade for %d day(s))",
            g_dailyStopReason, DailyLossNoTradeDays()));
         ArmDailyLossNoTradeCooldown(day0);
        }
      else
        {
         LogAlways(StringFormat(
            "Straddle: OPEN-BOOK HARD LOSS STOP - %s - STOP AND CLOSE (flat rest of day only; multi-day pause not armed)",
            g_dailyStopReason));
        }
      if(InpDailyLossNextDayLot2x)
        {
         g_pendingNextDayLot2x = true;
         PersistPendingNextDayLot2x(true);
            LogAlways(StringFormat("Straddle: NEXT DAY lots+cycle target x2 (daily profit +%.2f and loss -%.2f limits remain at configured mode)",
                                   EffectiveDailyProfitLimitUSD(),
                                   EffectiveDailyLossLimitUSD()));
        }
      BeginDailyStopAndClose(g_dailyStopReason);
     }
  }

bool IsDailyTradingStopped()
  {
   return g_dailyTradingStopped;
  }

//+------------------------------------------------------------------+
//| 1.1.20: stop NEW trading for the day AND force-close everything.  |
//| Bypasses cycleNet rescue-hold so red books still flatten.         |
//+------------------------------------------------------------------+
void BeginDailyStopAndClose(const string reason)
  {
   g_dailyTradingStopped = true;
   g_dailyStopForceClose = true;
   g_basketTearDown = false;
   ClearPersistedBasketTearDownTag();
   g_rescueHolding = false;
   ClearTrendRescueState(false);
   g_teardownSafeThreshold = 0.0;
   g_tearingDown = true;
   GlobalVariableDel(RHVar());
   ClearRescueAnchorBalance();
   ClearRescueHedgeTime();
   if(!GlobalVariableSet(TDVar(), (double)TimeCurrent()))
      Log(0, "Straddle: WARN could not set teardown marker for daily stop");
   PersistDailyStopMarker();
   PersistTearDownThreshold();
   LogAlways(StringFormat("Straddle: DAILY STOP AND CLOSE - %s", reason));
   ProcessTearDown();
  }

//+------------------------------------------------------------------+
//| Realized P/L of THIS cycle from deal history: close-side deals    |
//| (DEAL_ENTRY_OUT) for magic+symbol since cycle start, summing      |
//| profit + swap + commission + fee (commission/fee are booked       |
//| NEGATIVE for charges, so the signed sum is already net).          |
//| POSITION_COMMISSION is NOT used anywhere - it reads 0.            |
//+------------------------------------------------------------------+
double CycleRealized()
  {
   if(g_cycleStart <= 0 || !g_cycleStartTrusted)
      return 0.0;
   if(!HistorySelect(g_cycleStart, TimeCurrent()))   // must be called before deal reads
      return 0.0;
   double sum = 0.0;
   for(int i = HistoryDealsTotal() - 1; i >= 0; i--)
     {
      ulong deal = HistoryDealGetTicket(i);
      if(deal == 0)
         continue;
      if(HistoryDealGetInteger(deal, DEAL_MAGIC) != InpMagic)
         continue;
      if(HistoryDealGetString(deal, DEAL_SYMBOL) != g_sym)
         continue;
      if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal, DEAL_ENTRY) != DEAL_ENTRY_OUT)
         continue;
      // 0.0.44: a FLOATED leg keeps its "STR B#/S#" comment, so its OUT deal carries
      // that comment (NOT an orphan tag) and the filter above does NOT catch it. Key
      // off DEAL_POSITION_ID instead: a green-closed floated leg's position-id is in
      // g_floatClosedPositionIds, so its close never folds into the FRESH cycle's
      // realized (which would falsely trip target teardown on borrowed profit). Empty
      // set when InpUseFloatReanchor=false => byte-identical.
      if(g_floatClosedCount > 0 &&
         IsFloatPositionId((ulong)HistoryDealGetInteger(deal, DEAL_POSITION_ID)))
         continue;
      sum += HistoryDealGetDouble(deal, DEAL_PROFIT)
           + HistoryDealGetDouble(deal, DEAL_SWAP)
           + HistoryDealGetDouble(deal, DEAL_COMMISSION)
           + HistoryDealGetDouble(deal, DEAL_FEE);   // some brokers book under DEAL_FEE
     }
   return sum;
  }

//+------------------------------------------------------------------+
//| Cycle NET = banked (realized this cycle) + floating:              |
//|   floating = Sum(POSITION_PROFIT + POSITION_SWAP)                 |
//|              - InpCommissionPerLot * open lots (fallback for the  |
//|                not-yet-booked close-side cost; default 0)         |
//| This feeds the target teardown and boundary teardown diagnostics.  |
//+------------------------------------------------------------------+
double CycleNet()
  {
   // 0.0.42: cycle-scoped. The floating term is now CYCLE floating (whole-book
   // floating minus orphan floating); CycleRealized already excludes orphan close
   // deals. OpenFloatingPL (whole-book) and NetExposureLots (whole-book) are left
   // UNCHANGED for the account-level backstop/basket-TP. With no orphan present
   // CycleFloatingPL==OpenFloatingPL so CycleNet is byte-identical to 0.0.41.
   double cycleLots = 0.0;
   double floating = CycleFloatingPL(cycleLots);
   return CycleRealized() + floating;
  }

//+------------------------------------------------------------------+
//| Place one stop order at its FIXED grid level price (0.0.10) with  |
//| bounded retry / retcode branching.                                |
//| NO stop-loss / take-profit is EVER attached (SL=0.0, TP=0.0):     |
//| the per-leg trail and the net full-close are the only exits.      |
//| Strict slot rule: the sent pending price must remain the requested|
//| LevelPrice(side, level). If fresh Bid/Ask + stops/freeze makes    |
//| that fixed slot invalid, skip this tick instead of pushing the    |
//| order to a different price.                                       |
//| Sets g_abortBuild on hard limits (10019/10033/10034/10040) to     |
//| degrade gracefully and g_buildSkipWait on market-closed/disabled  |
//| codes (10017/10018/10026/10027) - NO per-level logging on skip.   |
//| A pending only counts as placed when the send succeeds, the server|
//| returns a clean placement retcode, and the selected order matches |
//| the fixed slot. An uncertain ticket is never re-sent immediately. |
//+------------------------------------------------------------------+
bool PlaceStopOrder(const bool isBuy, const double lots, const int level)
  {
   if(IsStopped())
     {
      g_buildSkipWait = true;
      return false;
     }

   const string comment = LegComment(isBuy, level);
   const double price = NormalizeDouble(LevelPrice(isBuy, level), g_digits);
   double sendBid = 0.0, sendAsk = 0.0;
   if(!ValidatePendingStopPriceForSend(isBuy, price, sendBid, sendAsk))
     {
      if(IsStopped())
        {
         g_buildSkipWait = true;
         return false;
        }
      LogFixedSlotSkip(StringFormat("Straddle: strict fixed grid price invalid for %s STOP L%d at send time - skipped",
                                    (isBuy ? "BUY" : "SELL"), level));
      return false;
     }

   StrActionOutcome outcome=ExecutePendingStop(isBuy,lots,price,comment);
   uint rc=outcome.retcode;
   ulong ticket=outcome.order;
   if(TradeOutcomeAccepted(outcome) && ticket!=0)
     {
      g_bookDirty=true;
      if(IsPendingPlacementSuccessRetcode(rc) &&
         ValidatePlacedStopOrder(ticket,isBuy,level,comment,price))
         return true;
      LogOp(StringFormat("Straddle: %s STOP level %d accepted/resulted but validation failed, ticket #%I64u, retcode %u (%s)",
                         (isBuy ? "BUY" : "SELL"),level,ticket,rc,outcome.result_comment));
      return false;
     }
   if(TradeOutcomeAccepted(outcome))
      return false;

   // 1.1.16: margin too low for another stop — STOP the whole populate pass.
   // Without this, KeepGridPendings + 60 levels retries EVERY empty slot EVERY
   // tick, each with a unique LogOp string => multi-GB journals / "stuck" tests.
   if(outcome.reason == REJECT_MARGIN_PROJECTION)
     {
       g_abortBuild = true;
       g_marginAbortBuild = true;
       g_marginAbortLevel = level;
       datetime until = TimeCurrent() + InpRetrySeconds;
      if(until > g_nextBuildTry)
         g_nextBuildTry = until;
      double freeM = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
      LogState(0, "margin-abort",
                StringFormat("Straddle: populate ABORT margin after pair preflight/send fail (first fail L%d %s freeMargin=%.0f) - skip remaining BUY/SELL pairs, backoff %ds",
                            level, (isBuy ? "BUY" : "SELL"), freeM, InpRetrySeconds),
               60);
      return false;
     }

   ENUM_RETCODE_ACTION action=ClassifyRetcode(rc);
   if(outcome.reason==REJECT_EXPOSURE_CAP || action==ACT_ABORT_CYCLE)
     {
      g_abortBuild=true;
      LogOp(StringFormat("Straddle: populate aborted at level %d, retcode %u (%s) - degrading to fewer slots",
                         level,rc,outcome.result_comment));
     }
   else if(outcome.reason==DEFER_MARKET_CLOSED || action==ACT_SKIP_WAIT)
      g_buildSkipWait=true;
   else if(outcome.state==REJECTED)
     {
      // 1.1.16: still log non-margin rejects, but only via LogOp throttle
      int currentLastError=GetLastError();
      LogOp(StringFormat("Straddle: %s STOP level %d failed, state=%s reason=%s terminal_result=%s retcode=%u retcode_external=%u stored_last_error=%d current_last_error=%d comment=%s",
                         (isBuy ? "BUY" : "SELL"),level,
                         EnumToString(outcome.state),EnumToString(outcome.reason),
                         (outcome.terminal_result ? "true" : "false"),
                         outcome.retcode,outcome.retcode_external,outcome.last_error,
                         currentLastError,outcome.result_comment));
     }
   return false;
  }

//+------------------------------------------------------------------+
//| 1.1.10: place one recovery stop (not grid level comment).         |
//+------------------------------------------------------------------+
bool PlaceBasketRecoveryStop(const bool isBuy, const double lots, const double price)
  {
   if(IsStopped() || TradeJamActive())
      return false;
   double sendBid = 0.0, sendAsk = 0.0;
   if(!ValidatePendingStopPriceForSend(isBuy, price, sendBid, sendAsk))
      return false;
   const string comment = BasketRecoveryComment(isBuy);
   StrActionOutcome outcome = ExecutePendingStop(isBuy, lots, price, comment);
   if(TradeOutcomeAccepted(outcome) && outcome.order != 0)
     {
      g_bookDirty = true;
      return true;
     }
   if(outcome.retcode == 0)
      NoteTradeJam(0, "basket-rcv");
   return false;
  }

bool BasketRecoveryPendingNear(const bool isBuy, const double price)
  {
   const double tick = TickSize();
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;
      string comment = OrderGetString(ORDER_COMMENT);
      if(!IsBasketRecoveryComment(comment))
         continue;
      ENUM_ORDER_TYPE type = (ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
      if(isBuy && type != ORDER_TYPE_BUY_STOP)
         continue;
      if(!isBuy && type != ORDER_TYPE_SELL_STOP)
         continue;
      double op = OrderGetDouble(ORDER_PRICE_OPEN);
      if(MathAbs(op - price) <= tick * 1.5)
         return true;
     }
   return false;
  }

int CountBasketRecoveryPendings()
  {
   int n = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;
      if(IsBasketRecoveryComment(OrderGetString(ORDER_COMMENT)))
         n++;
     }
   return n;
  }

//+------------------------------------------------------------------+
//| 1.1.10 BASKET BREAK-EVEN RECOVERY                                 |
//| When price rose and BUY stops filled (buy zone open), place       |
//| SELL-STOPs under market as price climbs — keep adding until       |
//| whole-book floating P/L >= target (default 0 = break-even).       |
//| Symmetric: net short -> BUY-STOPs above market as price falls.    |
//| Does not delete main grid STR B#/S# pendings.                     |
//+------------------------------------------------------------------+
void ProcessBasketBreakEvenRecovery()
  {
   if(!InpUseBasketRecovery || IsStopped())
      return;
   if(g_tearingDown || TradeJamActive())
      return;
   if(!CanTrade())
      return;

   double openLots = 0.0;
   double floating = OpenFloatingPL(openLots);
   double target = MoneyInput(InpBasketRecoveryTargetUSD);
   // Already recovered (floating at/above break-even target)
   if(floating >= target - 1.0e-6)
      return;
   if(CountMyPositions() <= 0)
      return;

   double buyLots = 0.0, sellLots = 0.0;
   NetExposureLots(buyLots, sellLots);
   // Need directional open exposure to recover
   if(buyLots <= 1.0e-8 && sellLots <= 1.0e-8)
      return;

   int maxR = InpBasketRecoveryMaxOrders;
   if(maxR < 1)
      maxR = 1;
   if(CountBasketRecoveryPendings() >= maxR)
      return;

   double bid = 0.0, ask = 0.0;
   if(!RefreshCurrentPrices(bid, ask) || bid <= 0.0 || ask <= 0.0)
      return;

   double step = 0.0;
   if(InpBasketRecoveryStepUSD > 0.0)
      step = PriceDistanceInput(InpBasketRecoveryStepUSD);
   if(step <= 0.0)
      step = CycleGridStepDistance();
   if(step <= 0.0)
      step = GridStepDistance();
   if(step <= 0.0)
      step = 1.0;

   double rawLot = (InpBasketRecoveryLot > 0.0
                    ? InpBasketRecoveryLot
                    : (IsAutoLotMode() ? InpAutoLotNear * AutoLotScaleFactor()
                                       : InpLotNear));
   double lots = NormalizeLot(rawLot);
   if(lots <= 0.0)
      return;

   // Net long (buys opened on rise) => place SELL stops in the zone under price
   // Net short => place BUY stops above price
   const bool recoverWithSellStops = (buyLots >= sellLots - 1.0e-8 && buyLots > 1.0e-8);
   const bool recoverWithBuyStops  = (sellLots > buyLots + 1.0e-8);

   if(!recoverWithSellStops && !recoverWithBuyStops)
      return;

   bool isBuyStop = recoverWithBuyStops; // buy-stop recovery for short bag
   // walk levels from market outward; place ONE new stop per tick
   for(int k = 1; k <= maxR; k++)
     {
      double price = 0.0;
      if(isBuyStop)
         price = SnapUp(ask + step * (double)k);
      else
         price = SnapDown(bid - step * (double)k);

      if(BasketRecoveryPendingNear(isBuyStop, price))
         continue;
      if(!PlaceBasketRecoveryStop(isBuyStop, lots, price))
         continue;

      LogState(1, "basket-rcv",
               StringFormat("Straddle: BASKET-RCV %s-stop @%s lots=%.2f float=%.1f target=%.1f buyL=%.2f sellL=%.2f",
                            (isBuyStop ? "BUY" : "SELL"),
                            DoubleToString(price, g_digits), lots, floating, target,
                            buyLots, sellLots),
               20);
      return; // one placement per tick
     }
  }

//+------------------------------------------------------------------+
//| Close one position with bounded retry (handles partial closes).   |
//| ACT_SKIP_WAIT / ACT_ABORT_CYCLE return SILENTLY (mirrors          |
//| PlaceStopOrder): re-issuing a close into a closed/disabled market |
//| once per stranded ticket per second reproduced the weekend log    |
//| flood. The teardown back-off (g_nextTearTry) owns the retry.      |
//| Deterministic ACT_FAIL codes (10013/10014/10044/10045/...) log    |
//| through the LogOp throttle - at most one identical line per       |
//| InpRetrySeconds (0.0.5 open-market flood fix).                    |
//+------------------------------------------------------------------+
bool CloseOnePosition(const ulong ticket)
  {
   if(TradeJamActive())
      return false;
   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      if(!PositionSelectByTicket(ticket))
        {
         if(g_floatCount > 0) RemoveFloatTicket(ticket);  // 0.0.44: prune closed float leg
         return true;                                   // already gone
        }
      double closeVolume=PositionGetDouble(POSITION_VOLUME);
      StrActionOutcome outcome=ExecutePositionReduction(ticket,closeVolume,"STR CLOSE");
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         g_bookDirty=true;
         if(!PositionSelectByTicket(ticket))
            return true;
         continue;
        }
      if(outcome.state==PENDING_RECONCILIATION)
        {
         NoteTradeJam(rc, "close-pending");
         return false;
        }
      // 1.1.7: empty success (retcode 0 / no deal) is a jam, not a hard fail-spam
      if(rc == 0 || (outcome.terminal_result && outcome.order == 0 && outcome.deal == 0 &&
                     outcome.state != COMPLETED))
        {
         NoteTradeJam(0, "close");
         Sleep(RetryBackoffDelayMs(attempt));
         continue;
        }
      switch(ClassifyRetcode(rc))
        {
         case ACT_SUCCESS:
            return false;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            NoteTradeJam(rc, "close");
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:    // market closed / trading disabled - expected, retry later, NO log
         case ACT_ABORT_CYCLE:  // hard limit - stop now, NO per-ticket log
            return false;
         default:                                       // genuinely unexpected codes only
            LogOp(StringFormat("Straddle: close of position #%I64u failed, retcode %u (%s)",
                               ticket,rc,outcome.result_comment));
            return false;
        }
     }
   bool closed = !PositionSelectByTicket(ticket);
   if(closed && g_floatCount > 0) RemoveFloatTicket(ticket);  // 0.0.44: prune closed float leg (basket-TP whole-book path)
   return closed;
  }

//+------------------------------------------------------------------+
//| Cleanup budget. Losing-ticket cleanup spends realized closed-leg  |
//| surplus only; floating basket profit cannot fund realized losses. |
//+------------------------------------------------------------------+
double RescueBank(const string reason)
  {
   if(reason == "")
      Log(2, "Straddle: rescue bank requested without a reason label");

   if(!g_rescueHolding)
      return 0.0;
   if(CountMyPositions() <= 0)
      return 0.0;
   if(!g_rescueAnchorTrusted || g_rescueAnchorBalance <= 0.0)
      return 0.0;

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double bank = balance - g_rescueAnchorBalance - MoneyInput(InpProfitReserveUSD) - MoneyInput(InpCleanupCostBufferUSD);
   return MathMax(0.0, bank);
  }

void LogRescueStatus(const string phase, const bool force)
  {
   if(!g_rescueHolding && !force)
      return;
   datetime now = TimeCurrent();
   if(!force)
     {
      if(InpRescueStatusLogSeconds <= 0)
         return;
      if(g_lastRescueStatusLog > 0 && (now - g_lastRescueStatusLog) < InpRescueStatusLogSeconds)
         return;
     }
   g_lastRescueStatusLog = now;

   double openLots = 0.0;
   double floating = OpenFloatingPL(openLots);
   double realized = CycleRealized();
   double cycleNet = realized + floating;
   double rescueBank = RescueBank("rescue-status");
   double cleanupBudget = g_rescueHolding
                          ? rescueBank
                          : (g_cycleStartTrusted
                             ? MathMax(0.0, realized - MoneyInput(InpProfitReserveUSD) - MoneyInput(InpCleanupCostBufferUSD))
                             : 0.0);

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   double margin = AccountInfoDouble(ACCOUNT_MARGIN);
   double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
   double marginLevel = AccountInfoDouble(ACCOUNT_MARGIN_LEVEL);
   int positions = CountMyPositions();
   int pendings = CountMyPendings();
   int rescueHedges = CountRescueHedges();

   Log(1, StringFormat("Straddle: rescue status [%s] balance %.2f equity %.2f margin %.2f freeMargin %.2f marginLevel %.2f%% rescueActive %s rescueMarker %s rescueAnchorTrusted %s anchorBalance %.2f cycleStartTrusted %s CycleRealized %.2f CycleNet %.2f floating %.2f rescueBank %.2f cleanupBudget %.2f positions %d pendings %d rescueHedges %d openLots %.2f",
                       phase,
                       balance, equity, margin, freeMargin, marginLevel,
                       (g_rescueHolding ? "yes" : "no"),
                       (GlobalVariableCheck(RHVar()) ? "yes" : "no"),
                       (g_rescueAnchorTrusted ? "yes" : "no"),
                       g_rescueAnchorBalance,
                       (g_cycleStartTrusted ? "yes" : "no"),
                       realized, cycleNet, floating, rescueBank, cleanupBudget,
                       positions, pendings, rescueHedges, openLots));

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;

      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      double profitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      Log(2, StringFormat("Straddle: rescue position [%s] ticket #%I64u type %s volume %.2f entry %s currentPnlSwap %.2f SL %s TP %s comment \"%s\"",
                          phase,
                          ticket,
                          (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                          PositionGetDouble(POSITION_VOLUME),
                          DoubleToString(PositionGetDouble(POSITION_PRICE_OPEN), g_digits),
                          profitAndSwap,
                          DoubleToString(PositionGetDouble(POSITION_SL), g_digits),
                          DoubleToString(PositionGetDouble(POSITION_TP), g_digits),
                          PositionGetString(POSITION_COMMENT)));
     }
  }

double ProfitFundedCleanupBudget(const string reason)
  {
   if(reason == "")
      Log(2, "Straddle: cleanup budget requested without a reason label");
   if(g_rescueHolding)
      return RescueBank(reason);

   // 1.1.8: equity-recovery budget — do not wait for large cycle surplus;
   // allow small funded nibble of worst losers so floating DD can shrink.
   if(reason == "equity-recovery" && EquityRecoveryActive())
     {
      double realized = (g_cycleStartTrusted ? CycleRealized() : 0.0);
      double surplus = realized - MoneyInput(InpCleanupCostBufferUSD);
      if(surplus < 0.0)
         surplus = 0.0;
      // Floor budget so recovery can always try at least a few min-lot closes
      // (still gated by VoluntaryLossCloseAllowed per ticket).
      double floorBudget = MoneyInput(MathMax(10.0, InpEquityRecoveryDDUSD * 0.15));
      return MathMax(surplus, floorBudget);
     }

   if(!g_cycleStartTrusted)
      return 0.0;
   double realized = CycleRealized();
   double budget = realized - MoneyInput(InpProfitReserveUSD) - MoneyInput(InpCleanupCostBufferUSD);
   return MathMax(0.0, budget);
  }

//+------------------------------------------------------------------+
//| Normalize a cleanup close volume down to broker step/min/max.     |
//| Unlike NormalizeLot(), this never clamps UP because that could    |
//| spend more loss than the current profit-funded budget permits.    |
//+------------------------------------------------------------------+
double NormalizeCloseLotsDown(double lots, const double maxVolume)
  {
   if(lots <= 0.0 || maxVolume <= 0.0)
      return 0.0;

   double vmin  = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   double vmax  = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MAX);
   double vstep = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP);

   if(vmax > 0.0 && lots > vmax)
      lots = vmax;
   if(lots > maxVolume)
      lots = maxVolume;
   if(vstep > 0.0)
      lots = MathFloor((lots + 1.0e-12) / vstep) * vstep;
   if(lots > maxVolume)
     {
      if(vstep > 0.0)
         lots = MathFloor((maxVolume + 1.0e-12) / vstep) * vstep;
      else
         lots = maxVolume;
     }

   lots = NormalizeDouble(lots, 8);
   if(lots <= 0.0)
      return 0.0;
   if(vmin > 0.0 && lots + 1.0e-8 < vmin)
      return 0.0;
   return lots;
  }

double TrendRescueCleanupEstimatedLoss(const ulong ticket,
                                       const double profitAndSwap,
                                       const double volume,
                                       const double closeLots)
  {
   if(volume <= 0.0 || closeLots <= 0.0)
      return 0.0;

   double proratedProfitAndSwap = profitAndSwap * (closeLots / volume);
   if(closeLots + 1.0e-8 >= volume)
      proratedProfitAndSwap = profitAndSwap;

   double coveredLoss = MathMax(0.0, -proratedProfitAndSwap);
   double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
   if(estimatedCloseCost < 0.0)
      estimatedCloseCost = 0.0;
   return coveredLoss + estimatedCloseCost;
  }

//+------------------------------------------------------------------+
//| Fit cleanup volume to the remaining loss budget after step floor. |
//+------------------------------------------------------------------+
double CleanupLotsWithinBudget(const double desiredLots,
                               const double maxVolume,
                               const double lossPerLot,
                               const double budget)
  {
   if(lossPerLot <= 0.0 || budget <= 0.0)
      return 0.0;

   double lots  = NormalizeCloseLotsDown(desiredLots, maxVolume);
   double vmin  = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   double vstep = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP);

   while(lots > 0.0 && lots * lossPerLot > budget + 1.0e-6)
     {
      if(vstep <= 0.0)
         return 0.0;
      lots = NormalizeDouble(lots - vstep, 8);
      if(vmin > 0.0 && lots + 1.0e-8 < vmin)
         return 0.0;
     }

   return NormalizeCloseLotsDown(lots, maxVolume);
  }

double CleanupChunkLots(const double volume,
                        const double lossPerLot,
                        const double budget)
  {
   if(volume <= 0.0 || lossPerLot <= 0.0 || budget <= 0.0)
      return 0.0;

   double vmin = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   double vstep = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP);
   double chunk = 0.01;
   if(vmin > 0.0)
      chunk = MathMax(chunk, vmin);
   if(vstep > 0.0)
      chunk = MathMax(chunk, vstep);

   chunk = NormalizeCloseLotsDown(chunk, volume);
   if(chunk <= 0.0)
      return 0.0;
   if(chunk * lossPerLot > budget + 1.0e-6)
      return 0.0;
   return chunk;
  }

bool CleanupCandidateBetter(const CleanupCandidate &a, const CleanupCandidate &b)
  {
   if(a.onLargestExposureSide != b.onLargestExposureSide)
      return a.onLargestExposureSide;
   if(a.openTime != b.openTime)
      return (a.openTime < b.openTime);
   if(MathAbs(a.swap - b.swap) > 1.0e-8)
      return (a.swap < b.swap);
   if(MathAbs(a.lossPerLot - b.lossPerLot) > 1.0e-8)
      return (a.lossPerLot < b.lossPerLot);
   if(MathAbs(a.loss - b.loss) > 1.0e-8)
      return (a.loss < b.loss);
   return (a.ticket < b.ticket);
  }

//+------------------------------------------------------------------+
//| Collect losing tickets for this EA, with rescue cleanup metadata. |
//+------------------------------------------------------------------+
int CollectCleanupCandidates(CleanupCandidate &candidates[])
  {
   ArrayResize(candidates, 0);
   double buyLots = 0.0, sellLots = 0.0;
   NetExposureLots(buyLots, sellLots);
   bool hasDirectionalBias = (MathAbs(buyLots - sellLots) > 1.0e-8);
   ENUM_POSITION_TYPE largestSide = (buyLots >= sellLots ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);

   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(IsFloatTicket(ticket)) // 0.0.44 NO-REALIZE: a floated leg is closed ONLY when green; never bank it at a loss
         continue;
      if(IsRescueHedgeComment(PositionGetString(POSITION_COMMENT)))
         continue;
      if(IsAveragingComment(PositionGetString(POSITION_COMMENT))) // 0.0.39: never select an AVG add for realized-loss cleanup
         continue;

      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;

      double loss = MathAbs(positionProfitAndSwap);
      double lossPerLot = loss / volume;
      if(lossPerLot <= 0.0)
         continue;

       ArrayResize(candidates, n + 1);
      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      candidates[n].ticket     = ticket;
      candidates[n].volume     = volume;
      candidates[n].loss       = loss;
      candidates[n].lossPerLot = lossPerLot;
      candidates[n].swap       = PositionGetDouble(POSITION_SWAP);
      candidates[n].openTime   = (datetime)PositionGetInteger(POSITION_TIME);
      candidates[n].type       = type;
      candidates[n].onLargestExposureSide = (hasDirectionalBias && type == largestSide);
      n++;
     }
   return n;
  }

//+------------------------------------------------------------------+
//| Sort cleanup candidates by rescue ladder priority.                |
//+------------------------------------------------------------------+
void SortCleanupCandidates(CleanupCandidate &candidates[])
  {
   int n = ArraySize(candidates);
   for(int i = 0; i < n - 1; i++)
     {
       int best = i;
       for(int j = i + 1; j < n; j++)
         {
          if(CleanupCandidateBetter(candidates[j], candidates[best]))
             best = j;
         }
      if(best != i)
        {
         CleanupCandidate tmp = candidates[i];
         candidates[i] = candidates[best];
         candidates[best] = tmp;
        }
     }
  }

//+------------------------------------------------------------------+
//| Ticket-based partial close for profit-funded cleanup.             |
//+------------------------------------------------------------------+
bool PrepareCleanupCloseRequest(const double requestedLots,
                                 const double budget,
                                 double &closeLots,
                                 double &estimatedLoss,
                                 const bool includeCloseCost)
  {
   if(requestedLots <= 0.0 || budget <= 0.0)
      return false;

   if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
      return false;
   if(PositionGetString(POSITION_SYMBOL) != g_sym)
      return false;
   ulong positionTicket = (ulong)PositionGetInteger(POSITION_TICKET);
   string comment = PositionGetString(POSITION_COMMENT);
   if(IsRescueHedgeComment(comment))
      return false;
   if(IsTrendRescueComment(comment))
      return false;
   if(IsAveragingComment(comment)) // 0.0.39: never realize a loss on an averaging add
      return false;

   double volume = PositionGetDouble(POSITION_VOLUME);
   if(volume <= 0.0)
      return false;

   double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   if(positionProfitAndSwap >= 0.0)
      return false;

   double fullLoss = -positionProfitAndSwap;
   if(fullLoss <= 0.0)
      return false;

   double lossPerLot = fullLoss / volume;
   if(lossPerLot <= 0.0)
      return false;

   double budgetLossPerLot = lossPerLot;
   if(includeCloseCost)
      budgetLossPerLot += EstimatedCloseCost(1.0, positionTicket);
   if(budgetLossPerLot <= 0.0)
      return false;

   double lots = CleanupLotsWithinBudget(requestedLots, volume, budgetLossPerLot, budget);
   double vmin = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   if(lots <= 0.0 || lots > volume + 1.0e-8 || (vmin > 0.0 && lots + 1.0e-8 < vmin))
      return false;

   double fullEstimatedLoss = (includeCloseCost
                               ? TrendRescueCleanupEstimatedLoss(positionTicket, positionProfitAndSwap, volume, volume)
                               : fullLoss);
   double remainingVolume = NormalizeDouble(volume - lots, 8);
   if(remainingVolume <= 1.0e-8)
     {
      if(fullEstimatedLoss > budget + 1.0e-6)
         {
          lots = NormalizeCloseLotsDown(volume - vmin, volume);
          remainingVolume = NormalizeDouble(volume - lots, 8);
        }
      else
        {
         lots = NormalizeDouble(volume, 8);
         remainingVolume = 0.0;
        }
     }

   if(vmin > 0.0 && remainingVolume > 1.0e-8 && remainingVolume + 1.0e-8 < vmin)
     {
      if(fullEstimatedLoss <= budget + 1.0e-6)
         {
          lots = NormalizeDouble(volume, 8);
          remainingVolume = 0.0;
        }
      else
        {
         lots = NormalizeCloseLotsDown(volume - vmin, volume);
         remainingVolume = NormalizeDouble(volume - lots, 8);
        }
     }

   if(lots <= 0.0 || lots > volume + 1.0e-8 || (vmin > 0.0 && lots + 1.0e-8 < vmin))
      return false;
   if(vmin > 0.0 && remainingVolume > 1.0e-8 && remainingVolume + 1.0e-8 < vmin)
      return false;

   estimatedLoss = (includeCloseCost
                    ? TrendRescueCleanupEstimatedLoss(positionTicket, positionProfitAndSwap, volume, lots)
                    : (remainingVolume <= 1.0e-8 ? fullLoss : lots * lossPerLot));
   if(estimatedLoss > budget + 1.0e-6)
      return false;

   closeLots = NormalizeDouble(lots, 8);
   return true;
  }

bool CloseCleanupPartial(const ulong ticket,
                          double &closeLots,
                          double &estimatedLoss,
                          const double budget,
                          const bool includeCloseCost)
  {
   if(closeLots <= 0.0)
      return false;

   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      if(!PositionSelectByTicket(ticket))
         return false;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         return false;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         return false;
      if(IsFloatTicket(ticket)) // 0.0.44 NO-REALIZE backstop: never realize a loss on a floated leg
         return false;
      string comment = PositionGetString(POSITION_COMMENT);
      if(IsRescueHedgeComment(comment))
         return false;
      if(IsTrendRescueComment(comment))
         return false;
      if(IsAveragingComment(comment)) // 0.0.39: never realize a loss on an averaging add
         return false;
      if(PositionGetDouble(POSITION_VOLUME) <= 0.0)
         return false;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         return false;

      double requestLots = closeLots;
      double requestLoss = estimatedLoss;
      if(!PrepareCleanupCloseRequest(requestLots, budget, closeLots, requestLoss, includeCloseCost))
         return false;

      double floorBudget = 0.0;
      if(!VoluntaryLossCloseAllowed("cleanup-close", ticket, requestLoss, 0.0, floorBudget))
         return false;
      if(requestLoss > MathMin(budget, floorBudget) + 1.0e-6)
         return false;

      StrActionOutcome outcome=ExecutePositionReduction(ticket,closeLots,"STR CLEAN");
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         g_bookDirty = true;   // 0.0.40 O1: cleanup partial close -> book mutated
         estimatedLoss = MathMax(0.0,-outcome.confirmed_net_money);
         InvalidateTrendRescueSnapshot();
         InvalidateTrendRescueBelowMinLotGuards();
         return true;
        }

      switch(ClassifyRetcode(rc))
        {
          case ACT_SUCCESS:
             return false;
          case ACT_RETRY_REFRESH:
             break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:
         case ACT_ABORT_CYCLE:
            return false;
          default:
             LogOp(StringFormat("Straddle: cleanup partial close of position #%I64u volume %.2f failed, retcode %u (%s)",
                                ticket,closeLots,rc,outcome.result_comment));
            return false;
        }
     }

   return false;
  }

//+------------------------------------------------------------------+
//| Profit-Funded Loss Reduction: spend only surplus profit to        |
//| partially close losing tickets. Returns true only if a cleanup    |
//| close request was accepted. This is not a hard loss cap.          |
//+------------------------------------------------------------------+
//+------------------------------------------------------------------+
//| 1.1.8: floating DD arms fast equity recovery.                     |
//+------------------------------------------------------------------+
bool EquityRecoveryActive()
  {
   if(!InpFastEquityRecovery)
      return false;
   double thr = MoneyInput(InpEquityRecoveryDDUSD);
   if(thr <= 0.0)
      thr = 80.0;
   double lots = 0.0;
   double fl = OpenFloatingPL(lots);
   // Also use balance-equity gap (restart-robust)
   double bal = AccountInfoDouble(ACCOUNT_BALANCE);
   double eq  = AccountInfoDouble(ACCOUNT_EQUITY);
   double gap = bal - eq;
   return (fl <= -thr || gap >= thr);
  }

//+------------------------------------------------------------------+
//| NeverRealize blocks red closes UNLESS fast equity recovery is on. |
//| 1.1.15: TREND-2X no longer bypasses this (balance dip on recovery).|
//+------------------------------------------------------------------+
bool RealizedLossClosesAllowed()
  {
   if(InpFastEquityRecovery && EquityRecoveryActive())
      return true;
   return !InpNeverRealizeLoss;
  }

// 1.1.8: recovery actions while equity is deep in the hole.
// Returns true => ManageCycle should skip normal populate/start this tick.
bool ProcessFastEquityRecovery()
  {
   if(!EquityRecoveryActive())
      return false;

   double openLots = 0.0;
   double fl = OpenFloatingPL(openLots);
   double gap = AccountInfoDouble(ACCOUNT_BALANCE) - AccountInfoDouble(ACCOUNT_EQUITY);

   // 1) 1.1.9: do NOT wipe grid pendings when KeepGridPendings (user wants stops on chart)
   if(!InpKeepGridPendings && CountMyPendings() > 0)
      DeleteAllPendings();

   // 2) Trail winners / bank green floats first
   double bid = 0.0, ask = 0.0;
   if(RefreshCurrentPrices(bid, ask) && bid > 0.0 && ask > 0.0)
     {
      if(InpUseTrailing && CountMyPositions() > 0)
        {
         UpdateLegTracking(bid, ask);
         ManagePerLegTrailing(bid, ask);
        }
     }
   if(InpUseFloatReanchor && g_floatCount > 0)
      FloatGreenClose();

   // 3) Funded loser cleanup (RealizedLossClosesAllowed is true in recovery)
   TryProfitFundedCleanup("equity-recovery");

   // 4) Still deep → float remaining cycle legs (no balance hit)
   fl = OpenFloatingPL(openLots);
   if(InpUseFloatReanchor && fl <= -MoneyInput(InpEquityRecoveryDDUSD) * 0.5)
     {
      if(CountMyPositions() - CountFloatPositions() > 0)
         FloatReanchor("Fast equity recovery float");
     }

   // 5) Residual red float → sleeper trend rescue
   if(g_floatCount > 0 && FloatFloatingPL() < -1.0e-6)
      TryEnterFloatSleeperRescue();

   LogState(1, "eq-recovery",
            StringFormat("Straddle: EQ-RECOVERY fl=%.0f gap=%.0f pos=%d f=%d pend=%d",
                         fl, gap, CountMyPositions(), g_floatCount, CountMyPendings()),
            60);
   return true;
  }

// Oldest open time among live float tickets (0 if none).
datetime OldestFloatPositionTime()
  {
   datetime oldest = 0;
   for(int i = 0; i < g_floatCount; i++)
     {
      ulong t = g_floatTickets[i];
      if(t == 0 || !PositionSelectByTicket(t))
         continue;
      datetime ot = (datetime)PositionGetInteger(POSITION_TIME);
      if(ot <= 0)
         continue;
      if(oldest == 0 || ot < oldest)
         oldest = ot;
     }
   return oldest;
  }

// Net float exposure: +1 net long float, -1 net short, 0 flat/unknown.
int FloatNetDirection()
  {
   double buyLots = 0.0, sellLots = 0.0;
   for(int i = 0; i < g_floatCount; i++)
     {
      ulong t = g_floatTickets[i];
      if(t == 0 || !PositionSelectByTicket(t))
         continue;
      double vol = PositionGetDouble(POSITION_VOLUME);
      if(vol <= 0.0)
         continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY)
         buyLots += vol;
      else
         sellLots += vol;
     }
   if(buyLots > sellLots + 1.0e-8)
      return 1;
   if(sellLots > buyLots + 1.0e-8)
      return -1;
   return 0;
  }

// True when floated bag is still red AND sleeper max age not exceeded.
// After InpFloatSleeperMaxHours, returns false so backtests cannot freeze for weeks.
bool FloatBagBlocksNewGrid()
  {
   if(!InpNeverRealizeLoss || !InpUseFloatReanchor)
      return false;
   if(g_floatCount <= 0)
      return false;
   double fpl = FloatFloatingPL();
   if(fpl >= -1.0e-6)
      return false; // green/flat float bag: green-close can clear; allow cycles

   // 1.1.6 sleeper timeout: resume trading after max hold (default 24h)
   int maxH = InpFloatSleeperMaxHours;
   if(maxH < 0)
      maxH = 0;
   if(maxH > 0)
     {
      datetime oldest = OldestFloatPositionTime();
      if(oldest > 0)
        {
         long ageSec = (long)(TimeCurrent() - oldest);
         if(ageSec >= (long)maxH * 3600L)
           {
            LogState(1, "float-sleeper-timeout",
                     StringFormat("Straddle: FLOAT SLEEPER TIMEOUT age=%dh >= %dh floatTickets=%d - allowing new cycle (float still open, green-close only)",
                                  (int)(ageSec / 3600L), maxH, g_floatCount),
                     300);
            return false;
           }
        }
     }
   return true;
  }

// 1.1.6/1.1.7: red float bag => enter Trend Rescue (not full 60-level restack).
// 1.1.7: also when cycle legs still exist (Jan19 mixed books never entered rescue).
bool TryEnterFloatSleeperRescue()
  {
   if(!InpUseTrendRescueMode || !InpUseFloatReanchor)
      return false;
   if(IsTrendRescueActive() || IsRescueHoldActive() || g_tearingDown)
      return false;
   if(TradeJamActive())
      return false;
   if(g_floatCount <= 0)
      return false;
   if(CountFloatPositions() <= 0)
      return false;

   double fpl = FloatFloatingPL();
   if(fpl >= -1.0e-6)
      return false;

   // 1.1.9: keep grid pendings on chart unless user disabled keep
   if(!InpKeepGridPendings && CountMyPendings() > 0)
      DeleteAllPendings();

   int floatDir = FloatNetDirection();
   // Short float bag red => price ran up => BUY recovery; long bag red => SELL recovery
   int direction = 0;
   if(floatDir < 0)
      direction = 1;
   else if(floatDir > 0)
      direction = -1;
   else
     {
      int bias = TrendOneSideBias();
      direction = (bias != 0 ? bias : 1);
     }

   double bid = 0.0, ask = 0.0;
   if(!RefreshCurrentPrices(bid, ask) || bid <= 0.0 || ask <= 0.0)
      return false;
   double mid = SnapPrice((bid + ask) * 0.5);
   double step = CycleGridStepDistance();
   if(step <= 0.0)
      step = GridStepDistance();
   if(step <= 0.0)
      step = MathMax(bid * 0.001, 1.0);
   double gridMin = mid - step * MathMax(1, InpGridLevels);
   double gridMax = mid + step * MathMax(1, InpGridLevels);

   LogState(1, "float-sleeper-rescue",
            StringFormat("Straddle: FLOAT SLEEPER RESCUE enter dir=%s floatTickets=%d floatPL=%.2f equity=%.2f (no full grid restack)",
                         (direction > 0 ? "BUY" : "SELL"), g_floatCount, fpl,
                         AccountInfoDouble(ACCOUNT_EQUITY)),
            60);
   EnterTrendRescue(direction, mid, gridMin, gridMax, fpl, CountMyPositions(), 0);
   return true;
  }

bool TryProfitFundedCleanup(const string reason)
  {
   if(!RealizedLossClosesAllowed())
      return false;
   if(!InpUseProfitFundedCleanup)
      return false;
   int minLosingPositions = (g_rescueHolding ? 1 : InpCleanupMinLosingPositions);
   if(minLosingPositions > 0 && CountLosingPositions() < minLosingPositions)
      return false;

   double budget = ProfitFundedCleanupBudget(reason);
   if(budget <= 0.0)
      return false;
   CleanupCandidate candidates[];
   int n = CollectCleanupCandidates(candidates);
   if(n <= 0)
      return false;
   if(minLosingPositions > 0 && n < minLosingPositions)
      return false;

   SortCleanupCandidates(candidates);

   bool closedAny = false;
   double remaining = budget;
   int cleanupActions = 0;
   double cleanupSpentEst = 0.0;
   for(int i = 0; i < n && remaining > 0.0; i++)
     {
      ulong ticket = candidates[i].ticket;
      if(!PositionSelectByTicket(ticket))
         continue;

      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;

       double lossPerLot = MathAbs(positionProfitAndSwap) / volume;
       if(lossPerLot <= 0.0)
          continue;

       double voluntaryBudget = VoluntaryLossBudget(MoneyInput(InpCleanupCostBufferUSD));
       double spendBudget = MathMin(remaining, voluntaryBudget);
       double closeLots = CleanupChunkLots(volume, lossPerLot, spendBudget);
       if(closeLots <= 0.0)
          continue;

       double estimatedLoss = closeLots * lossPerLot;
       if(estimatedLoss > spendBudget + 1.0e-6)
          continue;
       if(!VoluntaryLossCloseAllowed(StringFormat("profit-funded-voluntary_loss_floor-%s", reason), ticket,
                                     estimatedLoss, MoneyInput(InpCleanupCostBufferUSD),
                                     voluntaryBudget))
          continue;

       double before = remaining;
       if(g_rescueHolding)
          LogRescueStatus("before cleanup chunk", true);
       if(!CloseCleanupPartial(ticket, closeLots, estimatedLoss, spendBudget, false))
           continue;

       double cappedRemaining = before - estimatedLoss;
       if(cappedRemaining < 0.0)
          cappedRemaining = 0.0;
       remaining = MathMin(ProfitFundedCleanupBudget(reason), cappedRemaining);
       closedAny = true;
       cleanupActions++;
       cleanupSpentEst += estimatedLoss;

       // 1.1.3: per-chunk detail at debug; one level-1 summary after the loop
       Log(2, StringFormat("Straddle: profit-funded cleanup chunk close ticket #%I64u volume %.2f estimatedLoss %.2f budget %.2f->%.2f reason %s priority netSide=%s openTime=%s swap %.2f",
                           ticket, closeLots, estimatedLoss, before, remaining, reason,
                           (candidates[i].onLargestExposureSide ? "yes" : "no"),
                           TimeToString(candidates[i].openTime, TIME_DATE | TIME_SECONDS),
                           candidates[i].swap));
       if(g_rescueHolding)
          LogRescueStatus("after cleanup chunk", true);

       if(remaining <= 0.0)
          break;
      }

   if(closedAny)
      LogState(1, "cleanup-sum",
               StringFormat("Straddle: cleanup %s acts=%d spent=%.1f left=%.1f net=%.1f",
                            reason, cleanupActions, cleanupSpentEst, remaining, CycleNet()),
               30);

   return closedAny;
  }

double TrendRescueLoserCleanupBudget(const string reason)
  {
   if(reason == "")
      Log(2, "Straddle: trend rescue loser cleanup budget requested without a reason label");
   if(!g_trendRescueActive)
      return 0.0;
   if(CountMyPositions() <= 0)
      return 0.0;
   if(!g_rescueAnchorTrusted || g_rescueAnchorBalance <= 0.0)
      return 0.0;

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double budget = balance - g_rescueAnchorBalance - MoneyInput(InpProfitReserveUSD) - MoneyInput(InpTrendRescueCleanupBufferUSD);
   return MathMax(0.0, budget);
  }

int CollectTrendRescueCleanupCandidates(CleanupCandidate &candidates[])
  {
   ArrayResize(candidates, 0);
   double buyLots = 0.0, sellLots = 0.0;
   NetExposureLots(buyLots, sellLots);
   bool hasDirectionalBias = (MathAbs(buyLots - sellLots) > 1.0e-8);
   ENUM_POSITION_TYPE largestSide = (buyLots >= sellLots ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);

   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(IsFloatTicket(ticket)) // 0.0.44 NO-REALIZE: a floated leg is closed ONLY when green; never bank it at a loss
         continue;

      string comment = PositionGetString(POSITION_COMMENT);
      if(IsTrendRescueComment(comment))
         continue;
      if(IsRescueHedgeComment(comment))
         continue;
      if(IsAveragingComment(comment)) // 0.0.39: never realize a loss on an averaging add
         continue;

      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;

      double loss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, volume);
      double lossPerLot = loss / volume;
      if(lossPerLot <= 0.0)
         continue;

      ArrayResize(candidates, n + 1);
      candidates[n].ticket     = ticket;
      candidates[n].volume     = volume;
      candidates[n].loss       = loss;
      candidates[n].lossPerLot = lossPerLot;
      candidates[n].swap       = PositionGetDouble(POSITION_SWAP);
      candidates[n].openTime   = (datetime)PositionGetInteger(POSITION_TIME);
      candidates[n].type       = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      candidates[n].onLargestExposureSide = (hasDirectionalBias && candidates[n].type == largestSide);
      n++;
     }

   return n;
  }

bool TrendRescueCleanupCandidateBetter(const CleanupCandidate &a, const CleanupCandidate &b)
  {
   return CleanupCandidateBetter(a, b);
  }

void SortTrendRescueCleanupCandidates(CleanupCandidate &candidates[])
  {
   int n = ArraySize(candidates);
   for(int i = 0; i < n - 1; i++)
     {
      int best = i;
      for(int j = i + 1; j < n; j++)
        {
         if(TrendRescueCleanupCandidateBetter(candidates[j], candidates[best]))
            best = j;
        }
      if(best != i)
        {
         CleanupCandidate tmp = candidates[i];
         candidates[i] = candidates[best];
         candidates[best] = tmp;
        }
     }
  }

bool TryTrendRescueProfitFundedLoserCleanup(const string reason)
  {
   if(!RealizedLossClosesAllowed())
      return false;
   if(!g_trendRescueActive || !InpUseTrendRescueLoserCleanup)
      return false;
   if(InpTrendRescueCleanupMaxActionsPerTick <= 0)
     {
      Log(2, StringFormat("Straddle: trend rescue loser cleanup skipped: reason=max_actions maxActions=%d",
                          InpTrendRescueCleanupMaxActionsPerTick));
      return false;
     }
   bool closedAny = false;
   int actions = 0;
   while(actions < InpTrendRescueCleanupMaxActionsPerTick)
     {
      double balance = AccountInfoDouble(ACCOUNT_BALANCE);
      double budget = TrendRescueLoserCleanupBudget(reason);
      CleanupCandidate candidates[];
      int n = CollectTrendRescueCleanupCandidates(candidates);

      LogFmt(2, StringFormat("Straddle: trend rescue loser cleanup budget: reason=%s anchorBalance=%.2f balance=%.2f reserve=%.2f buffer=%.2f budget=%.2f oldLosingCandidates=%d actions=%d/%d",
                          reason, g_rescueAnchorBalance, balance, MoneyInput(InpProfitReserveUSD),
                          MoneyInput(InpTrendRescueCleanupBufferUSD), budget, n, actions,
                          InpTrendRescueCleanupMaxActionsPerTick));

      if(n <= 0)
        {
         TrendRescueBelowMinLotGuardClear("old-grid");
         Log(2, "Straddle: trend rescue loser cleanup skipped: reason=no_candidates");
         LogTrendRescueCleanupDiag("old-grid", "no_candidates");
         break;
        }
      if(budget <= 0.0)
        {
         TrendRescueBelowMinLotGuardClear("old-grid");
         Log(2, StringFormat("Straddle: trend rescue loser cleanup skipped: reason=no_budget anchorBalance=%.2f balance=%.2f reserve=%.2f buffer=%.2f",
                              g_rescueAnchorBalance, balance, MoneyInput(InpProfitReserveUSD),
                              MoneyInput(InpTrendRescueCleanupBufferUSD)));
         LogTrendRescueCleanupDiag("old-grid", "no_budget",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
         break;
        }

      SortTrendRescueCleanupCandidates(candidates);
      string belowMinGuardKey = TrendRescueBelowMinLotStateKey("old-grid", candidates, budget,
                                                               MoneyInput(InpTrendRescueCleanupBufferUSD));

      bool acted = false;
      bool belowMinLot = false;
      bool closeFailed = false;
      for(int i = 0; i < n; i++)
        {
         if(candidates[i].loss > budget + 1.0e-6)
            break;
         ulong ticket = candidates[i].ticket;
         if(!PositionSelectByTicket(ticket))
            continue;
         string comment = PositionGetString(POSITION_COMMENT);
         if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
            continue;
          double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
          if(positionProfitAndSwap >= 0.0)
             continue;

          double closeLots = PositionGetDouble(POSITION_VOLUME);
          if(closeLots <= 0.0)
             continue;
          double volume = closeLots;
          double requestedVolume = closeLots;
          double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, closeLots);
          if(estimatedLoss > budget + 1.0e-6)
             continue;
          double floorBudget = 0.0;
          if(!VoluntaryLossCloseAllowed(StringFormat("trend-rescue-loser-voluntary_loss_floor-%s", reason),
                                        ticket, estimatedLoss,
                                        MoneyInput(InpTrendRescueCleanupBufferUSD),
                                        floorBudget))
             continue;
          double closeBudget = MathMin(budget, floorBudget);
          if(estimatedLoss > closeBudget + 1.0e-6)
             continue;
          double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
          ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
          double before = closeBudget;
          if(CloseCleanupPartial(ticket, closeLots, estimatedLoss, closeBudget, true))
            {
             actions++;
             closedAny = true;
             acted = true;
             double after = TrendRescueLoserCleanupBudget(reason);
             estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
             if(closeLots + 1.0e-8 >= requestedVolume)
                Log(1, StringFormat("Straddle: trend rescue loser cleanup full close: ticket #%I64u type %s lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s",
                                    ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                    closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
             else
                Log(1, StringFormat("Straddle: trend rescue loser cleanup partial close: ticket #%I64u type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s full_close_reduced=yes",
                                    ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                    closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
             break;
            }

          closeFailed = true;
          LogOp(StringFormat("Straddle: trend rescue loser cleanup skipped: reason=close_failed mode=full ticket #%I64u lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f",
                             ticket, closeLots, estimatedLoss, estimatedCloseCost, before));
         }

      if(acted)
         continue;

      if(!InpTrendRescueUsePartialLoserCleanup)
        {
         TrendRescueBelowMinLotGuardClear("old-grid");
         Log(2, "Straddle: trend rescue loser cleanup skipped: reason=partial_disabled");
         break;
        }

      if(TrendRescueBelowMinLotGuardActive("old-grid", belowMinGuardKey))
        {
         LogTrendRescueCleanupDiag("old-grid", "below_min_lot_guard",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
         break;
        }

      for(int i = 0; i < n; i++)
        {
         ulong ticket = candidates[i].ticket;
         if(!PositionSelectByTicket(ticket))
            continue;
         string comment = PositionGetString(POSITION_COMMENT);
         if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
            continue;
         double volume = PositionGetDouble(POSITION_VOLUME);
         if(volume <= 0.0)
            continue;
         double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         if(positionProfitAndSwap >= 0.0)
            continue;

          double lossPerLot = MathAbs(positionProfitAndSwap) / volume;
          if(lossPerLot <= 0.0)
             continue;
          double floorBudget = VoluntaryLossBudget(MoneyInput(InpTrendRescueCleanupBufferUSD));
          double closeBudget = MathMin(budget, floorBudget);
          double budgetLossPerLot = lossPerLot + EstimatedCloseCost(1.0, ticket);
          double closeLots = CleanupChunkLots(volume, budgetLossPerLot, closeBudget);
          if(closeLots <= 0.0)
            {
             belowMinLot = true;
             continue;
            }

          double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, closeLots);
          if(estimatedLoss > closeBudget + 1.0e-6)
             continue;
          if(!VoluntaryLossCloseAllowed(StringFormat("trend-rescue-loser-voluntary_loss_floor-%s", reason),
                                        ticket, estimatedLoss,
                                        MoneyInput(InpTrendRescueCleanupBufferUSD),
                                        floorBudget))
             continue;
          double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);

          ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
          double before = closeBudget;
          if(CloseCleanupPartial(ticket, closeLots, estimatedLoss, closeBudget, true))
            {
             actions++;
             closedAny = true;
             acted = true;
             double after = TrendRescueLoserCleanupBudget(reason);
             estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
             Log(1, StringFormat("Straddle: trend rescue loser cleanup partial close: ticket #%I64u type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s",
                                 ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                 closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
             break;
            }

          closeFailed = true;
          LogOp(StringFormat("Straddle: trend rescue loser cleanup skipped: reason=close_failed mode=partial ticket #%I64u lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f",
                             ticket, closeLots, estimatedLoss, estimatedCloseCost, before));
         }

      if(acted)
         continue;

      if(belowMinLot)
        {
         TrendRescueBelowMinLotGuardRecord("old-grid", belowMinGuardKey);
         Log(2, StringFormat("Straddle: trend rescue loser cleanup skipped: reason=below_min_lot budget=%.2f oldLosingCandidates=%d",
                              budget, n));
         LogTrendRescueCleanupDiag("old-grid", "below_min_lot",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
        }
      else if(closeFailed)
        {
         TrendRescueBelowMinLotGuardClear("old-grid");
         Log(2, StringFormat("Straddle: trend rescue loser cleanup skipped: reason=close_failed budget=%.2f oldLosingCandidates=%d",
                              budget, n));
         LogTrendRescueCleanupDiag("old-grid", "close_failed",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
        }
      else
        {
         TrendRescueBelowMinLotGuardClear("old-grid");
         Log(2, StringFormat("Straddle: trend rescue loser cleanup skipped: reason=no_fundable_action budget=%.2f oldLosingCandidates=%d",
                             budget, n));
         LogTrendRescueCleanupDiag("old-grid", "no_fundable_action",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
        }
      break;
     }

   if(actions >= InpTrendRescueCleanupMaxActionsPerTick)
      Log(2, StringFormat("Straddle: trend rescue loser cleanup skipped: reason=max_actions actions=%d maxActions=%d",
                          actions, InpTrendRescueCleanupMaxActionsPerTick));

   return closedAny;
  }

void InitializeProtectedProfitAnchor()
  {
   g_protectedProfitAnchorBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   Log(1, StringFormat("Straddle: protected profit cleanup anchor initialized: anchorBalance=%.2f floor=%.2f buffer=%.2f",
                       g_protectedProfitAnchorBalance,
                       MoneyInput(InpProtectedProfitFloorUSD),
                       MoneyInput(InpProtectedProfitCleanupBufferUSD)));
  }

double ProtectedProfitCleanupBudget(const string reason)
  {
   if(reason == "")
      Log(2, "Straddle: protected profit cleanup budget requested without a reason label");
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double budget = balance - g_protectedProfitAnchorBalance -
                   MoneyInput(InpProtectedProfitFloorUSD) -
                   MoneyInput(InpProtectedProfitCleanupBufferUSD);
   return MathMax(0.0, budget);
  }

bool TryProtectedProfitCleanup(const string reason)
  {
   if(!RealizedLossClosesAllowed())
      return false;
   if(!g_trendRescueActive || !InpUseProtectedProfitCleanup)
      return false;
   if(InpProtectedProfitCleanupMaxActionsPerTick <= 0)
     {
      Log(2, StringFormat("Straddle: protected profit cleanup skipped: reason=max_actions maxActions=%d",
                          InpProtectedProfitCleanupMaxActionsPerTick));
      return false;
     }
   bool closedAny = false;
   int actions = 0;
   while(actions < InpProtectedProfitCleanupMaxActionsPerTick)
     {
      double balance = AccountInfoDouble(ACCOUNT_BALANCE);
      double budget = ProtectedProfitCleanupBudget(reason);
      CleanupCandidate candidates[];
      int n = CollectTrendRescueCleanupCandidates(candidates);

      LogFmt(2, StringFormat("Straddle: protected profit cleanup budget: reason=%s anchorBalance=%.2f balance=%.2f protectedFloor=%.2f buffer=%.2f budget=%.2f oldLosingCandidates=%d actions=%d/%d",
                          reason, g_protectedProfitAnchorBalance, balance,
                          MoneyInput(InpProtectedProfitFloorUSD),
                          MoneyInput(InpProtectedProfitCleanupBufferUSD),
                          budget, n, actions,
                          InpProtectedProfitCleanupMaxActionsPerTick));

      if(n <= 0)
        {
         TrendRescueBelowMinLotGuardClear("protected");
         Log(2, "Straddle: protected profit cleanup skipped: reason=no_candidates");
         LogTrendRescueCleanupDiag("protected", "no_candidates");
         break;
        }
      if(budget <= 0.0)
        {
         TrendRescueBelowMinLotGuardClear("protected");
         Log(2, StringFormat("Straddle: protected profit cleanup skipped: reason=no_budget anchorBalance=%.2f balance=%.2f protectedFloor=%.2f buffer=%.2f",
                              g_protectedProfitAnchorBalance, balance,
                             MoneyInput(InpProtectedProfitFloorUSD),
                             MoneyInput(InpProtectedProfitCleanupBufferUSD)));
         LogTrendRescueCleanupDiag("protected", "no_budget",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
         break;
        }

      SortTrendRescueCleanupCandidates(candidates);
      string belowMinGuardKey = TrendRescueBelowMinLotStateKey("protected", candidates, budget,
                                                               MoneyInput(InpProtectedProfitCleanupBufferUSD));

      bool acted = false;
      bool belowMinLot = false;
      bool closeFailed = false;
      for(int i = 0; i < n; i++)
        {
         if(candidates[i].loss > budget + 1.0e-6)
            break;
         ulong ticket = candidates[i].ticket;
         if(!PositionSelectByTicket(ticket))
            continue;
         string comment = PositionGetString(POSITION_COMMENT);
         if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
            continue;
         double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         if(positionProfitAndSwap >= 0.0)
            continue;

         double closeLots = PositionGetDouble(POSITION_VOLUME);
         if(closeLots <= 0.0)
            continue;
         double volume = closeLots;
         double requestedVolume = closeLots;
         double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, closeLots);
         if(estimatedLoss > budget + 1.0e-6)
            continue;
         double floorBudget = 0.0;
         if(!VoluntaryLossCloseAllowed(StringFormat("protected-cleanup-voluntary_loss_floor-%s", reason),
                                       ticket, estimatedLoss,
                                       MoneyInput(InpProtectedProfitCleanupBufferUSD),
                                       floorBudget))
            continue;
         double closeBudget = MathMin(budget, floorBudget);
         if(estimatedLoss > closeBudget + 1.0e-6)
            continue;
         double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
         ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
         double before = closeBudget;
         if(CloseCleanupPartial(ticket, closeLots, estimatedLoss, closeBudget, true))
           {
            actions++;
            closedAny = true;
            acted = true;
            double after = ProtectedProfitCleanupBudget(reason);
            estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
            if(closeLots + 1.0e-8 >= requestedVolume)
               Log(1, StringFormat("Straddle: protected profit cleanup full close: ticket #%I64u type %s lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s",
                                   ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                   closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
            else
               Log(1, StringFormat("Straddle: protected profit cleanup partial close: ticket #%I64u type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s full_close_reduced=yes",
                                   ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                   closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
            break;
           }

         closeFailed = true;
         LogOp(StringFormat("Straddle: protected profit cleanup skipped: reason=close_failed mode=full ticket #%I64u lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f",
                            ticket, closeLots, estimatedLoss, estimatedCloseCost, before));
        }

      if(acted)
         continue;

      if(TrendRescueBelowMinLotGuardActive("protected", belowMinGuardKey))
        {
         LogTrendRescueCleanupDiag("protected", "below_min_lot_guard",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
         break;
        }

      for(int i = 0; i < n; i++)
        {
         ulong ticket = candidates[i].ticket;
         if(!PositionSelectByTicket(ticket))
            continue;
         string comment = PositionGetString(POSITION_COMMENT);
         if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
            continue;
         double volume = PositionGetDouble(POSITION_VOLUME);
         if(volume <= 0.0)
            continue;
         double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         if(positionProfitAndSwap >= 0.0)
            continue;

         double lossPerLot = MathAbs(positionProfitAndSwap) / volume;
         if(lossPerLot <= 0.0)
            continue;
         double floorBudget = VoluntaryLossBudget(MoneyInput(InpProtectedProfitCleanupBufferUSD));
         double closeBudget = MathMin(budget, floorBudget);
         double budgetLossPerLot = lossPerLot + EstimatedCloseCost(1.0, ticket);
         double closeLots = CleanupChunkLots(volume, budgetLossPerLot, closeBudget);
         if(closeLots <= 0.0)
           {
            belowMinLot = true;
            continue;
           }

         double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, closeLots);
         if(estimatedLoss > closeBudget + 1.0e-6)
            continue;
         if(!VoluntaryLossCloseAllowed(StringFormat("protected-cleanup-voluntary_loss_floor-%s", reason),
                                       ticket, estimatedLoss,
                                       MoneyInput(InpProtectedProfitCleanupBufferUSD),
                                       floorBudget))
            continue;
         double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);

         ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
         double before = closeBudget;
         if(CloseCleanupPartial(ticket, closeLots, estimatedLoss, closeBudget, true))
           {
            actions++;
            closedAny = true;
            acted = true;
            double after = ProtectedProfitCleanupBudget(reason);
            estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
            Log(1, StringFormat("Straddle: protected profit cleanup partial close: ticket #%I64u type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s",
                                ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
            break;
           }

         closeFailed = true;
         LogOp(StringFormat("Straddle: protected profit cleanup skipped: reason=close_failed mode=partial ticket #%I64u lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f",
                            ticket, closeLots, estimatedLoss, estimatedCloseCost, before));
        }

      if(acted)
         continue;

      if(belowMinLot)
        {
         TrendRescueBelowMinLotGuardRecord("protected", belowMinGuardKey);
         Log(2, StringFormat("Straddle: protected profit cleanup skipped: reason=below_min_lot budget=%.2f oldLosingCandidates=%d",
                              budget, n));
         LogTrendRescueCleanupDiag("protected", "below_min_lot",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
        }
      else if(closeFailed)
        {
         TrendRescueBelowMinLotGuardClear("protected");
         Log(2, StringFormat("Straddle: protected profit cleanup skipped: reason=close_failed budget=%.2f oldLosingCandidates=%d",
                             budget, n));
         LogTrendRescueCleanupDiag("protected", "close_failed",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
        }
      else
        {
         TrendRescueBelowMinLotGuardClear("protected");
         Log(2, StringFormat("Straddle: protected profit cleanup skipped: reason=no_fundable_action budget=%.2f oldLosingCandidates=%d",
                             budget, n));
         LogTrendRescueCleanupDiag("protected", "no_fundable_action",
                                   StringFormat("budget=%.2f candidates=%d", budget, n));
        }
      break;
     }

   if(actions >= InpProtectedProfitCleanupMaxActionsPerTick)
      Log(2, StringFormat("Straddle: protected profit cleanup skipped: reason=max_actions actions=%d maxActions=%d",
                          actions, InpProtectedProfitCleanupMaxActionsPerTick));

   return closedAny;
  }

double TrendRescueEntryCleanupBudget(const string reason)
  {
   if(reason == "")
      Log(2, "Straddle: trend rescue entry cleanup budget requested without a reason label");
   if(!g_trendRescueActive)
      return 0.0;
   if(CountMyPositions() <= 0)
      return 0.0;
   if(!g_rescueAnchorTrusted || g_rescueAnchorBalance <= 0.0)
      return 0.0;

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double budget = balance - g_rescueAnchorBalance - MoneyInput(InpProfitReserveUSD) - MoneyInput(InpTrendRescueEntryCleanupBufferUSD);
   return MathMax(0.0, budget);
  }

int CollectTrendRescueEntryCleanupCandidates(CleanupCandidate &candidates[],
                                             const bool onlySameDirection = false,
                                             const bool isBuy = true)
  {
   ArrayResize(candidates, 0);
   ENUM_POSITION_TYPE trendType = (isBuy ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);

   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;

      string comment = PositionGetString(POSITION_COMMENT);
      if(!IsTrendRescueComment(comment))
         continue;

      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      if(onlySameDirection && type != trendType)
         continue;
      datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
      if(TrendRescueEntryCleanupProtected(type, openTime, onlySameDirection, isBuy))
         continue;

      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;

      double loss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, volume);
      double lossPerLot = loss / volume;
      if(lossPerLot <= 0.0)
         continue;

      ArrayResize(candidates, n + 1);
      candidates[n].ticket     = ticket;
      candidates[n].volume     = volume;
      candidates[n].loss       = loss;
      candidates[n].lossPerLot = lossPerLot;
      candidates[n].swap       = PositionGetDouble(POSITION_SWAP);
      candidates[n].openTime   = openTime;
      candidates[n].type       = type;
      candidates[n].onLargestExposureSide = false;
      n++;
     }

   return n;
  }

void TrendRescueEntryCleanupSummary(const bool onlySameDirection,
                                    const bool isBuy,
                                    int &redCount,
                                    double &estimatedLoss,
                                    double &budget,
                                    bool &fundable)
  {
   redCount = 0;
   estimatedLoss = 0.0;
   budget = TrendRescueEntryCleanupBudget("entry-summary");
   fundable = false;

   CleanupCandidate candidates[];
   int n = CollectTrendRescueEntryCleanupCandidates(candidates, onlySameDirection, isBuy);
   redCount = n;
   if(n <= 0)
      return;

   SortTrendRescueCleanupCandidates(candidates);
   for(int i = 0; i < n; i++)
      estimatedLoss += candidates[i].loss;

   fundable = (redCount > 0 && estimatedLoss <= budget + 1.0e-6);
  }

bool HasUnfundedSameDirectionTrendRescueEntries(const bool isBuy,
                                                int &redCount,
                                                double &estimatedLoss,
                                                double &budget)
  {
   bool fundable = false;
   TrendRescueEntryCleanupSummary(true, isBuy, redCount, estimatedLoss, budget, fundable);
   return (redCount > 0 && !fundable);
  }

int TrendRescueEffectiveEntryMinAgeSec()
  {
   if(InpTrendRescueEntryMinAgeSec <= 0)
      return 0;

   double coverageGap = TrendRescueCoverageGap();
   if(coverageGap <= 0.0)
      return InpTrendRescueEntryMinAgeSec;

   double pressure = TrendRescueAdaptivePressure(coverageGap);
   if(pressure <= 0.0)
      return InpTrendRescueEntryMinAgeSec;

   int floorAge = InpTrendRescueEntryMinAgeSec / 4;
   if(floorAge < 60)
      floorAge = 60;

   int effectiveAge = (int)MathRound((double)InpTrendRescueEntryMinAgeSec -
                                     ((double)(InpTrendRescueEntryMinAgeSec - floorAge) * pressure));
   if(effectiveAge < floorAge)
      effectiveAge = floorAge;
   if(effectiveAge > InpTrendRescueEntryMinAgeSec)
      effectiveAge = InpTrendRescueEntryMinAgeSec;
   return effectiveAge;
  }

bool TrendRescueEntryCleanupProtected(const ENUM_POSITION_TYPE type,
                                      const datetime openTime,
                                      const bool onlySameDirection,
                                      const bool isBuy)
  {
   ENUM_POSITION_TYPE trendType = (isBuy ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);
   bool activeDirectionEntry = (type == trendType);
   if(onlySameDirection && !activeDirectionEntry)
      return true;
   if(!activeDirectionEntry)
      return false;
   int effectiveAge = TrendRescueEffectiveEntryMinAgeSec();
   if(effectiveAge <= 0)
      return false;
   datetime now = TimeCurrent();
   if(openTime <= 0 || now <= 0)
      return true;
   return ((now - openTime) < effectiveAge);
  }

bool PrepareTrendRescueEntryCleanupCloseRequest(const double requestedLots,
                                                const double budget,
                                                double &closeLots,
                                                double &estimatedLoss)
  {
   if(requestedLots <= 0.0 || budget <= 0.0)
      return false;

   if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
      return false;
   if(PositionGetString(POSITION_SYMBOL) != g_sym)
      return false;
   ulong positionTicket = (ulong)PositionGetInteger(POSITION_TICKET);
   string comment = PositionGetString(POSITION_COMMENT);
   if(!IsTrendRescueComment(comment))
      return false;

   double volume = PositionGetDouble(POSITION_VOLUME);
   if(volume <= 0.0)
      return false;

   double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   if(positionProfitAndSwap >= 0.0)
      return false;

   double fullLoss = -positionProfitAndSwap;
   if(fullLoss <= 0.0)
      return false;

   double lossPerLot = fullLoss / volume;
   if(lossPerLot <= 0.0)
      return false;

   double budgetLossPerLot = lossPerLot + EstimatedCloseCost(1.0, positionTicket);
   if(budgetLossPerLot <= 0.0)
      return false;

   double lots = CleanupLotsWithinBudget(requestedLots, volume, budgetLossPerLot, budget);
   double vmin = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   if(lots <= 0.0 || lots > volume + 1.0e-8 || (vmin > 0.0 && lots + 1.0e-8 < vmin))
      return false;

   double fullEstimatedLoss = TrendRescueCleanupEstimatedLoss(positionTicket, positionProfitAndSwap, volume, volume);
   double remainingVolume = NormalizeDouble(volume - lots, 8);
   if(remainingVolume <= 1.0e-8)
     {
      if(fullEstimatedLoss > budget + 1.0e-6)
        {
         lots = NormalizeCloseLotsDown(volume - vmin, volume);
         remainingVolume = NormalizeDouble(volume - lots, 8);
        }
      else
        {
         lots = NormalizeDouble(volume, 8);
         remainingVolume = 0.0;
        }
     }

   if(vmin > 0.0 && remainingVolume > 1.0e-8 && remainingVolume + 1.0e-8 < vmin)
     {
      if(fullEstimatedLoss <= budget + 1.0e-6)
        {
         lots = NormalizeDouble(volume, 8);
         remainingVolume = 0.0;
        }
      else
        {
         lots = NormalizeCloseLotsDown(volume - vmin, volume);
         remainingVolume = NormalizeDouble(volume - lots, 8);
        }
     }

   if(lots <= 0.0 || lots > volume + 1.0e-8 || (vmin > 0.0 && lots + 1.0e-8 < vmin))
      return false;
   if(vmin > 0.0 && remainingVolume > 1.0e-8 && remainingVolume + 1.0e-8 < vmin)
      return false;

   estimatedLoss = TrendRescueCleanupEstimatedLoss(positionTicket, positionProfitAndSwap, volume, lots);
   if(estimatedLoss > budget + 1.0e-6)
      return false;

   closeLots = NormalizeDouble(lots, 8);
   return true;
  }

bool CloseTrendRescueEntryCleanupPartial(const ulong ticket,
                                         double &closeLots,
                                         double &estimatedLoss,
                                         const double budget)
  {
   if(closeLots <= 0.0)
      return false;

   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      if(!PositionSelectByTicket(ticket))
         return false;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         return false;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         return false;
      if(IsFloatTicket(ticket)) // 0.0.44 NO-REALIZE backstop: never realize a loss on a floated leg
         return false;
      string comment = PositionGetString(POSITION_COMMENT);
      if(!IsTrendRescueComment(comment))
         return false;
      if(PositionGetDouble(POSITION_VOLUME) <= 0.0)
         return false;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         return false;

      double requestLots = closeLots;
      double requestLoss = estimatedLoss;
      if(!PrepareTrendRescueEntryCleanupCloseRequest(requestLots, budget, closeLots, requestLoss))
         return false;

      double floorBudget = 0.0;
      if(!VoluntaryLossCloseAllowed("trend-entry-cleanup-close", ticket, requestLoss, 0.0, floorBudget))
         return false;
      if(requestLoss > MathMin(budget, floorBudget) + 1.0e-6)
         return false;

      StrActionOutcome outcome=ExecutePositionReduction(ticket,closeLots,"STR TR CLEAN");
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         g_bookDirty = true;   // 0.0.40 O1: trend-rescue entry cleanup partial close -> book mutated
         estimatedLoss = MathMax(0.0,-outcome.confirmed_net_money);
         InvalidateTrendRescueSnapshot();
         InvalidateTrendRescueBelowMinLotGuards();
         return true;
        }

      switch(ClassifyRetcode(rc))
        {
         case ACT_SUCCESS:
            return false;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:
         case ACT_ABORT_CYCLE:
            return false;
         default:
            LogOp(StringFormat("Straddle: trend rescue entry cleanup partial close of position #%I64u volume %.2f failed, retcode %u (%s)",
                               ticket,closeLots,rc,outcome.result_comment));
            return false;
        }
     }

   return false;
  }

bool TryTrendRescueEntryProfitFundedCleanup(const string reason)
  {
   if(!RealizedLossClosesAllowed())
      return false;
   if(!g_trendRescueActive || !InpUseTrendRescueEntryLoserCleanup)
      return false;
   if(InpTrendRescueEntryCleanupMaxActionsPerTick <= 0)
     {
      Log(2, StringFormat("Straddle: trend rescue entry cleanup skipped: reason=max_actions maxActions=%d",
                          InpTrendRescueEntryCleanupMaxActionsPerTick));
      return false;
     }
   bool closedAny = false;
   int actions = 0;
   while(actions < InpTrendRescueEntryCleanupMaxActionsPerTick)
     {
      double balance = AccountInfoDouble(ACCOUNT_BALANCE);
      double budget = TrendRescueEntryCleanupBudget(reason);
      CleanupCandidate candidates[];
      int n = CollectTrendRescueEntryCleanupCandidates(candidates, false, IsTrendRescueBuy());
      double totalEstimatedLoss = 0.0;
      for(int i = 0; i < n; i++)
         totalEstimatedLoss += candidates[i].loss;

      LogFmt(2, StringFormat("Straddle: trend rescue entry cleanup budget: reason=%s anchorBalance=%.2f balance=%.2f reserve=%.2f buffer=%.2f budget=%.2f redEntryCandidates=%d estimatedLoss=%.2f actions=%d/%d",
                          reason, g_rescueAnchorBalance, balance, MoneyInput(InpProfitReserveUSD),
                          MoneyInput(InpTrendRescueEntryCleanupBufferUSD), budget, n, totalEstimatedLoss,
                          actions, InpTrendRescueEntryCleanupMaxActionsPerTick));

      if(n <= 0)
        {
         TrendRescueBelowMinLotGuardClear("trend-entry");
         Log(2, "Straddle: trend rescue entry cleanup skipped: reason=no_candidates");
         LogTrendRescueCleanupDiag("trend-entry", "no_candidates");
         break;
        }
      if(budget <= 0.0)
        {
         TrendRescueBelowMinLotGuardClear("trend-entry");
         Log(2, StringFormat("Straddle: trend rescue entry cleanup skipped: reason=no_budget estimatedLoss=%.2f budget=%.2f redEntryCandidates=%d anchorBalance=%.2f balance=%.2f reserve=%.2f buffer=%.2f",
                              totalEstimatedLoss, budget, n, g_rescueAnchorBalance,
                             balance, MoneyInput(InpProfitReserveUSD), MoneyInput(InpTrendRescueEntryCleanupBufferUSD)));
         LogTrendRescueCleanupDiag("trend-entry", "no_budget",
                                   StringFormat("budget=%.2f estimatedLoss=%.2f candidates=%d", budget, totalEstimatedLoss, n));
         break;
        }

      SortTrendRescueCleanupCandidates(candidates);
      string belowMinGuardKey = TrendRescueBelowMinLotStateKey("trend-entry", candidates, budget,
                                                               MoneyInput(InpTrendRescueEntryCleanupBufferUSD),
                                                               "|estimatedLoss=" + TrendRescueMoneyStateKey(totalEstimatedLoss));

      bool acted = false;
      bool belowMinLot = false;
      bool closeFailed = false;
      for(int i = 0; i < n; i++)
        {
         if(candidates[i].loss > budget + 1.0e-6)
            break;
         ulong ticket = candidates[i].ticket;
         if(!PositionSelectByTicket(ticket))
            continue;
         string comment = PositionGetString(POSITION_COMMENT);
         if(!IsTrendRescueComment(comment))
            continue;
         double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         if(positionProfitAndSwap >= 0.0)
            continue;

         double closeLots = PositionGetDouble(POSITION_VOLUME);
         if(closeLots <= 0.0)
            continue;
         double volume = closeLots;
         double requestedVolume = closeLots;
         double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, closeLots);
         if(estimatedLoss > budget + 1.0e-6)
            continue;
         double floorBudget = 0.0;
         if(!VoluntaryLossCloseAllowed(StringFormat("trend-entry-cleanup-voluntary_loss_floor-%s", reason),
                                       ticket, estimatedLoss,
                                       MoneyInput(InpTrendRescueEntryCleanupBufferUSD),
                                       floorBudget))
            continue;
         double closeBudget = MathMin(budget, floorBudget);
         if(estimatedLoss > closeBudget + 1.0e-6)
            continue;
         double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
         ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
         double before = closeBudget;
         if(CloseTrendRescueEntryCleanupPartial(ticket, closeLots, estimatedLoss, closeBudget))
           {
            actions++;
            closedAny = true;
            acted = true;
            double after = TrendRescueEntryCleanupBudget(reason);
            estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
            if(closeLots + 1.0e-8 >= requestedVolume)
               Log(1, StringFormat("Straddle: trend rescue entry cleanup full close: ticket #%I64u type %s lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s",
                                   ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                   closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
            else
               Log(1, StringFormat("Straddle: trend rescue entry cleanup partial close: ticket #%I64u type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s full_close_reduced=yes",
                                   ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                   closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
            break;
           }

         closeFailed = true;
         LogOp(StringFormat("Straddle: trend rescue entry cleanup skipped: reason=close_failed mode=full ticket #%I64u lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f",
                            ticket, closeLots, estimatedLoss, estimatedCloseCost, before));
        }

      if(acted)
         continue;

      if(!InpTrendRescueEntryUsePartialCleanup)
        {
         TrendRescueBelowMinLotGuardClear("trend-entry");
         Log(2, StringFormat("Straddle: trend rescue entry cleanup skipped: reason=no_budget partial_disabled=yes estimatedLoss=%.2f budget=%.2f redEntryCandidates=%d",
                              totalEstimatedLoss, budget, n));
         LogTrendRescueCleanupDiag("trend-entry", "partial_disabled",
                                   StringFormat("budget=%.2f estimatedLoss=%.2f candidates=%d", budget, totalEstimatedLoss, n));
         break;
        }

      if(TrendRescueBelowMinLotGuardActive("trend-entry", belowMinGuardKey))
        {
         LogTrendRescueCleanupDiag("trend-entry", "below_min_lot_guard",
                                   StringFormat("budget=%.2f estimatedLoss=%.2f candidates=%d",
                                                budget, totalEstimatedLoss, n));
         break;
        }

      for(int i = 0; i < n; i++)
        {
         ulong ticket = candidates[i].ticket;
         if(!PositionSelectByTicket(ticket))
            continue;
         string comment = PositionGetString(POSITION_COMMENT);
         if(!IsTrendRescueComment(comment))
            continue;
         double volume = PositionGetDouble(POSITION_VOLUME);
         if(volume <= 0.0)
            continue;
         double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         if(positionProfitAndSwap >= 0.0)
            continue;

         double lossPerLot = MathAbs(positionProfitAndSwap) / volume;
         if(lossPerLot <= 0.0)
            continue;
         double floorBudget = VoluntaryLossBudget(MoneyInput(InpTrendRescueEntryCleanupBufferUSD));
         double closeBudget = MathMin(budget, floorBudget);
         double budgetLossPerLot = lossPerLot + EstimatedCloseCost(1.0, ticket);
         double closeLots = CleanupChunkLots(volume, budgetLossPerLot, closeBudget);
         if(closeLots <= 0.0)
           {
            belowMinLot = true;
            continue;
           }

         double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, closeLots);
         if(estimatedLoss > closeBudget + 1.0e-6)
            continue;
         if(!VoluntaryLossCloseAllowed(StringFormat("trend-entry-cleanup-voluntary_loss_floor-%s", reason),
                                       ticket, estimatedLoss,
                                       MoneyInput(InpTrendRescueEntryCleanupBufferUSD),
                                       floorBudget))
            continue;
         double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);

         ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
         double before = closeBudget;
         if(CloseTrendRescueEntryCleanupPartial(ticket, closeLots, estimatedLoss, closeBudget))
           {
            actions++;
            closedAny = true;
            acted = true;
            double after = TrendRescueEntryCleanupBudget(reason);
            estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
            Log(1, StringFormat("Straddle: trend rescue entry cleanup partial close: ticket #%I64u type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f reason=%s",
                                ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                closeLots, estimatedLoss, estimatedCloseCost, before, after, reason));
            break;
           }

         closeFailed = true;
         LogOp(StringFormat("Straddle: trend rescue entry cleanup skipped: reason=close_failed mode=partial ticket #%I64u lots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f",
                            ticket, closeLots, estimatedLoss, estimatedCloseCost, before));
        }

      if(acted)
         continue;

      if(belowMinLot)
        {
         TrendRescueBelowMinLotGuardRecord("trend-entry", belowMinGuardKey);
         Log(2, StringFormat("Straddle: trend rescue entry cleanup skipped: reason=below_min_lot budget=%.2f estimatedLoss=%.2f redEntryCandidates=%d",
                              budget, totalEstimatedLoss, n));
         LogTrendRescueCleanupDiag("trend-entry", "below_min_lot",
                                   StringFormat("budget=%.2f estimatedLoss=%.2f candidates=%d", budget, totalEstimatedLoss, n));
        }
      else if(closeFailed)
        {
         TrendRescueBelowMinLotGuardClear("trend-entry");
         Log(2, StringFormat("Straddle: trend rescue entry cleanup skipped: reason=close_failed budget=%.2f estimatedLoss=%.2f redEntryCandidates=%d",
                             budget, totalEstimatedLoss, n));
         LogTrendRescueCleanupDiag("trend-entry", "close_failed",
                                   StringFormat("budget=%.2f estimatedLoss=%.2f candidates=%d", budget, totalEstimatedLoss, n));
        }
      else
        {
         TrendRescueBelowMinLotGuardClear("trend-entry");
         Log(2, StringFormat("Straddle: trend rescue entry cleanup skipped: reason=no_budget estimatedLoss=%.2f budget=%.2f redEntryCandidates=%d smallestEstimatedLoss=%.2f",
                             totalEstimatedLoss, budget, n, candidates[0].loss));
         LogTrendRescueCleanupDiag("trend-entry", "no_fundable_action",
                                   StringFormat("budget=%.2f estimatedLoss=%.2f candidates=%d smallestEstimatedLoss=%.2f",
                                                budget, totalEstimatedLoss, n, candidates[0].loss));
        }
      break;
     }

   if(actions >= InpTrendRescueEntryCleanupMaxActionsPerTick)
      Log(2, StringFormat("Straddle: trend rescue entry cleanup skipped: reason=max_actions actions=%d maxActions=%d",
                          actions, InpTrendRescueEntryCleanupMaxActionsPerTick));

   return closedAny;
  }

double TrendRescuePairCleanupBudget(const string reason)
  {
   if(reason == "")
      Log(2, "Straddle: trend rescue pair cleanup budget requested without a reason label");
   if(!g_trendRescueActive)
      return 0.0;
   if(CountMyPositions() <= 0)
      return 0.0;
   if(!g_rescueAnchorTrusted || g_rescueAnchorBalance <= 0.0)
      return 0.0;

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double budget = balance - g_rescueAnchorBalance - MoneyInput(InpProfitReserveUSD) - MoneyInput(InpPairCleanupBufferUSD);
   return MathMax(0.0, budget);
  }

int CollectTrendRescuePairProfitCandidates(TrendRescueProfitCandidate &candidates[])
  {
   ArrayResize(candidates, 0);

   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(!IsTrendRescueComment(PositionGetString(POSITION_COMMENT)))
         continue;

      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double profitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      double estimatedCloseCost = EstimatedCloseCost(volume, ticket);
      if(profitAndSwap < MoneyInput(InpPairCleanupMinProfitUSD) + estimatedCloseCost)
         continue;

      ArrayResize(candidates, n + 1);
      candidates[n].ticket = ticket;
      candidates[n].volume = volume;
      candidates[n].profit = profitAndSwap;
      candidates[n].estimatedCloseCost = estimatedCloseCost;
      candidates[n].openTime = (datetime)PositionGetInteger(POSITION_TIME);
      candidates[n].type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      n++;
     }

   return n;
  }

bool TrendRescuePairProfitBetter(const TrendRescueProfitCandidate &a,
                                 const TrendRescueProfitCandidate &b)
  {
   if(MathAbs(a.profit - b.profit) > 1.0e-8)
      return (a.profit > b.profit);
   if(a.openTime != b.openTime)
      return (a.openTime < b.openTime);
   return (a.ticket < b.ticket);
  }

void SortTrendRescuePairProfitCandidates(TrendRescueProfitCandidate &candidates[])
  {
   int n = ArraySize(candidates);
   for(int i = 0; i < n - 1; i++)
     {
      int best = i;
      for(int j = i + 1; j < n; j++)
        {
         if(TrendRescuePairProfitBetter(candidates[j], candidates[best]))
            best = j;
        }
      if(best != i)
        {
         TrendRescueProfitCandidate tmp = candidates[i];
         candidates[i] = candidates[best];
         candidates[best] = tmp;
        }
     }
  }

bool PrepareTrendRescuePairLoserTicket(const ulong ticket,
                                       const bool isTrendEntry,
                                       const double budget,
                                       double &closeLots,
                                       double &estimatedLoss,
                                       double &estimatedCloseCost,
                                       bool &belowMinLot)
  {
   closeLots = 0.0;
   estimatedLoss = 0.0;
   estimatedCloseCost = 0.0;
   belowMinLot = false;
   if(ticket == 0 || budget <= 0.0)
      return false;
   if(!PositionSelectByTicket(ticket))
      return false;
   if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
      return false;
   if(PositionGetString(POSITION_SYMBOL) != g_sym)
      return false;

   string comment = PositionGetString(POSITION_COMMENT);
   if(isTrendEntry)
     {
      if(!IsTrendRescueComment(comment))
         return false;
     }
   else
     {
      if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
         return false;
     }

   double volume = PositionGetDouble(POSITION_VOLUME);
   if(volume <= 0.0)
      return false;
   double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   if(positionProfitAndSwap >= 0.0)
      return false;

   double fullEstimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, volume);
   double requestedLots = volume;
   if(fullEstimatedLoss > budget + 1.0e-6)
     {
      double lossPerLot = MathAbs(positionProfitAndSwap) / volume;
      double budgetLossPerLot = lossPerLot + EstimatedCloseCost(1.0, ticket);
      requestedLots = CleanupChunkLots(volume, budgetLossPerLot, budget);
      if(requestedLots <= 0.0)
        {
         belowMinLot = true;
         return false;
        }
     }

   double preparedLots = requestedLots;
   double preparedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, preparedLots);
   bool prepared = false;
   if(isTrendEntry)
      prepared = PrepareTrendRescueEntryCleanupCloseRequest(requestedLots, budget, preparedLots, preparedLoss);
   else
      prepared = PrepareCleanupCloseRequest(requestedLots, budget, preparedLots, preparedLoss, true);
   if(!prepared)
     {
      if(requestedLots > 0.0)
         belowMinLot = true;
      return false;
     }

   double refreshedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, preparedLots);
   if(refreshedLoss > budget + 1.0e-6)
      return false;

   closeLots = preparedLots;
   estimatedLoss = refreshedLoss;
   estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
   return (closeLots > 0.0);
  }

bool SelectTrendRescuePairLoserFromCandidates(CleanupCandidate &candidates[],
                                              const bool isTrendEntry,
                                              const double budget,
                                              ulong &ticket,
                                              double &closeLots,
                                              double &estimatedLoss,
                                              double &estimatedCloseCost,
                                              bool &belowMinLot)
  {
   int n = ArraySize(candidates);
   for(int i = 0; i < n; i++)
     {
      double candidateLots = 0.0;
      double candidateLoss = 0.0;
      double candidateCloseCost = 0.0;
      bool candidateBelowMinLot = false;
      if(PrepareTrendRescuePairLoserTicket(candidates[i].ticket, isTrendEntry, budget,
                                           candidateLots, candidateLoss, candidateCloseCost,
                                           candidateBelowMinLot))
        {
         ticket = candidates[i].ticket;
         closeLots = candidateLots;
         estimatedLoss = candidateLoss;
         estimatedCloseCost = candidateCloseCost;
         return true;
        }
      if(candidateBelowMinLot)
         belowMinLot = true;
     }

   return false;
  }

bool SelectTrendRescuePairLoser(const double budget,
                                bool &isTrendEntry,
                                ulong &ticket,
                                double &closeLots,
                                double &estimatedLoss,
                                double &estimatedCloseCost,
                                bool &belowMinLot)
  {
   ticket = 0;
   closeLots = 0.0;
   estimatedLoss = 0.0;
   estimatedCloseCost = 0.0;
   belowMinLot = false;

   CleanupCandidate oldLosers[];
   int oldCount = CollectTrendRescueCleanupCandidates(oldLosers);
   SortTrendRescueCleanupCandidates(oldLosers);

   CleanupCandidate redTrendEntries[];
   int redCount = CollectTrendRescueEntryCleanupCandidates(redTrendEntries, false, IsTrendRescueBuy());
   SortTrendRescueCleanupCandidates(redTrendEntries);

   if(InpPairCleanupPreferOldGridLosers)
      {
       isTrendEntry = false;
       if(SelectTrendRescuePairLoserFromCandidates(oldLosers, false, budget, ticket,
                                                   closeLots, estimatedLoss, estimatedCloseCost,
                                                   belowMinLot))
          return true;
       isTrendEntry = true;
       if(SelectTrendRescuePairLoserFromCandidates(redTrendEntries, true, budget, ticket,
                                                   closeLots, estimatedLoss, estimatedCloseCost,
                                                   belowMinLot))
          return true;
      }
   else
     {
       isTrendEntry = false;
       if(oldCount >= redCount &&
          SelectTrendRescuePairLoserFromCandidates(oldLosers, false, budget, ticket,
                                                   closeLots, estimatedLoss, estimatedCloseCost,
                                                   belowMinLot))
          return true;
       isTrendEntry = true;
       if(SelectTrendRescuePairLoserFromCandidates(redTrendEntries, true, budget, ticket,
                                                   closeLots, estimatedLoss, estimatedCloseCost,
                                                   belowMinLot))
          return true;
       isTrendEntry = false;
       if(SelectTrendRescuePairLoserFromCandidates(oldLosers, false, budget, ticket,
                                                   closeLots, estimatedLoss, estimatedCloseCost,
                                                   belowMinLot))
          return true;
      }

   return false;
  }

bool CloseTrendRescuePairProfitTicket(const ulong ticket,
                                      double &estimatedProfit,
                                      double &estimatedCloseCost)
  {
   estimatedProfit = 0.0;
   estimatedCloseCost = 0.0;
   if(ticket == 0)
      return false;

   double balanceBeforeClose = AccountInfoDouble(ACCOUNT_BALANCE);
   bool acceptedClose = false;
   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      if(!PositionSelectByTicket(ticket))
         return (acceptedClose ||
                 AccountInfoDouble(ACCOUNT_BALANCE) > balanceBeforeClose + 1.0e-6);
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         return false;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         return false;
      if(!IsTrendRescueComment(PositionGetString(POSITION_COMMENT)))
         return false;

      double volume = PositionGetDouble(POSITION_VOLUME);
      double profitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      estimatedCloseCost = EstimatedCloseCost(volume, ticket);
      if(profitAndSwap < MoneyInput(InpPairCleanupMinProfitUSD) + estimatedCloseCost)
         return false;
      estimatedProfit = profitAndSwap;

      StrActionOutcome outcome=ExecutePositionReduction(ticket,volume,"STR TR PROFIT");
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         g_bookDirty = true;   // 0.0.40 O1: trend-rescue pair profit ticket closed -> book mutated
         acceptedClose = true;
         InvalidateTrendRescueSnapshot();
         InvalidateTrendRescueBelowMinLotGuards();
         if(!PositionSelectByTicket(ticket))
            return true;
         if(AccountInfoDouble(ACCOUNT_BALANCE) > balanceBeforeClose + 1.0e-6)
            return true;
         continue;
        }

      switch(ClassifyRetcode(rc))
         {
         case ACT_SUCCESS:
            return false;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:
         case ACT_ABORT_CYCLE:
            return false;
         default:
            LogOp(StringFormat("Straddle: trend rescue pair cleanup profit close failed: ticket #%I64u estimatedProfit %.2f retcode %u (%s)",
                               ticket,estimatedProfit,rc,outcome.result_comment));
            return false;
        }
     }

   return (acceptedClose ||
           AccountInfoDouble(ACCOUNT_BALANCE) > balanceBeforeClose + 1.0e-6);
  }

bool CloseTrendRescuePairLoser(const ulong ticket,
                               const bool isTrendEntry,
                               double &closeLots,
                               double &estimatedLoss,
                               const double budget)
  {
   if(isTrendEntry)
      return CloseTrendRescueEntryCleanupPartial(ticket, closeLots, estimatedLoss, budget);
   return CloseCleanupPartial(ticket, closeLots, estimatedLoss, budget, true);
  }

void CarryTrendRescuePairOrphanedReserve(const double reserveBudget,
                                         const string detail,
                                         const ulong profitTicket,
                                         const ulong originalLoserTicket,
                                         const double balanceAfterProfit)
  {
   double reserve = NormalizeAccountMoney(MathMax(0.0, reserveBudget));
   if(reserve <= 0.0)
      return;

   double floorBudget = VoluntaryLossBudget(MoneyInput(InpPairCleanupBufferUSD));
   double bookedBudget = TrendRescuePairCleanupBudget("pair-reserve-carry");
   double reserveCap = MathMin(floorBudget, bookedBudget);
   if(reserveCap > 0.0)
      reserve = MathMin(reserve, reserveCap);

   double accumulatedReserve = NormalizeAccountMoney(g_deferredPairProfitReserve + reserve);
   if(reserveCap > 0.0)
      accumulatedReserve = MathMin(accumulatedReserve, reserveCap);
   g_deferredPairProfitReserve = NormalizeAccountMoney(accumulatedReserve);
   g_deferredPairProfitReserveTime = TimeCurrent();
   LogOp(StringFormat("Straddle: trend rescue pair cleanup reserve carried: reason=orphaned_pair_profit_reserve detail=%s profitTicket #%I64u originalLoserTicket #%I64u addedReserve=%.2f orphanedPairProfitReserve=%.2f reserveCap=%.2f floorBudget=%.2f bookedBudget=%.2f balanceAfterProfit=%.2f reserveTime=%I64d",
                      detail, profitTicket, originalLoserTicket,
                      reserve, g_deferredPairProfitReserve, reserveCap,
                      floorBudget, bookedBudget, balanceAfterProfit,
                      (long)g_deferredPairProfitReserveTime));
  }

double TrendRescuePairOrphanedReserveBudget(const string reason)
  {
   if(!OrphanedPairProfitReservePending(reason))
      return 0.0;
   double bookedBudget = TrendRescuePairCleanupBudget(reason);
   double floorBudget = VoluntaryLossBudget(MoneyInput(InpPairCleanupBufferUSD));
   return MathMax(0.0, MathMin(g_deferredPairProfitReserve, MathMin(bookedBudget, floorBudget)));
  }

bool TryTrendRescuePairOrphanedReserveCleanup(const string reason,
                                              int &actions,
                                              bool &closedAny)
  {
   if(!OrphanedPairProfitReservePending(reason))
      return false;
   if(actions >= InpPairCleanupMaxActionsPerTick)
      return false;

   double reserveBudget = TrendRescuePairOrphanedReserveBudget(reason);
   if(reserveBudget <= 0.0)
     {
      LogTrendRescueCleanupDiag("pair-reserve", "no_budget",
                                StringFormat("orphanedPairProfitReserve=%.2f bookedBudget=%.2f floorBudget=%.2f",
                                             g_deferredPairProfitReserve,
                                             TrendRescuePairCleanupBudget(reason),
                                             VoluntaryLossBudget(MoneyInput(InpPairCleanupBufferUSD))));
      return false;
     }

   bool loserIsTrendEntry = false;
   ulong loserTicket = 0;
   double closeLots = 0.0;
   double estimatedLoss = 0.0;
   double estimatedCloseCost = 0.0;
   bool belowMinLot = false;
   if(!SelectTrendRescuePairLoser(reserveBudget, loserIsTrendEntry, loserTicket,
                                  closeLots, estimatedLoss, estimatedCloseCost,
                                  belowMinLot))
     {
      LogTrendRescueCleanupDiag("pair-reserve",
                                (belowMinLot ? "needs_more_budget" : "no_fundable_loser"),
                                StringFormat("orphanedPairProfitReserve=%.2f reserveBudget=%.2f",
                                             g_deferredPairProfitReserve, reserveBudget));
      return false;
     }

   if(!PositionSelectByTicket(loserTicket))
     {
      LogTrendRescueCleanupDiag("pair-reserve", "loser_missing",
                                StringFormat("ticket #%I64u orphanedPairProfitReserve=%.2f reserveBudget=%.2f",
                                             loserTicket, g_deferredPairProfitReserve, reserveBudget));
      return false;
     }

   double loserVolume = PositionGetDouble(POSITION_VOLUME);
   double loserProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   double refreshedEstimatedLoss = TrendRescueCleanupEstimatedLoss(loserTicket, loserProfitAndSwap,
                                                                   loserVolume, closeLots);
   if(refreshedEstimatedLoss > reserveBudget + 1.0e-6)
     {
      LogTrendRescueCleanupDiag("pair-reserve", "refreshed_loss_exceeded",
                                StringFormat("ticket #%I64u refreshedLoss=%.2f reserveBudget=%.2f orphanedPairProfitReserve=%.2f",
                                             loserTicket, refreshedEstimatedLoss,
                                             reserveBudget, g_deferredPairProfitReserve));
      return false;
     }
   estimatedLoss = refreshedEstimatedLoss;
   estimatedCloseCost = EstimatedCloseCost(closeLots, loserTicket);

   double budgetBeforeLoser = reserveBudget;
   if(CloseTrendRescuePairLoser(loserTicket, loserIsTrendEntry,
                                closeLots, estimatedLoss, reserveBudget))
     {
      actions++;
      closedAny = true;
      ResetOrphanedPairProfitReserve();
      double budgetAfterLoser = TrendRescuePairCleanupBudget(reason);
      LogAlways(StringFormat("Straddle: trend rescue pair cleanup orphan reserve spent: loserTicket #%I64u loserKind=%s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f reserveBudget %.2f budgetAfter %.2f result=closed reason=%s",
                             loserTicket, (loserIsTrendEntry ? "trend-entry" : "old-grid"),
                             closeLots, estimatedLoss, estimatedCloseCost,
                             budgetBeforeLoser, budgetAfterLoser, reason));
      return true;
     }

   LogTrendRescueCleanupDiag("pair-reserve", "loser_close_failed",
                             StringFormat("ticket #%I64u closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f reserveBudget=%.2f orphanedPairProfitReserve=%.2f",
                                          loserTicket, closeLots, estimatedLoss,
                                          estimatedCloseCost, budgetBeforeLoser,
                                          g_deferredPairProfitReserve));
   return false;
  }

long StaleTradeMinAgeSec()
  {
   return g_staleTradeMinAgeSeconds;
  }

bool PositionAgeStale(const datetime openTime)
  {
   const long minAgeSec=StaleTradeMinAgeSec();
   if(minAgeSec <= 0)
      return true;
   datetime now = TimeCurrent();
   if(openTime <= 0 || now <= 0)
      return false;
   return ((now - openTime) >= minAgeSec);
  }

int CollectStaleOldGridCleanupCandidates(CleanupCandidate &candidates[])
  {
   ArrayResize(candidates, 0);
   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;

      if(IsFloatTicket(ticket)) // 0.0.44 NO-REALIZE: a floated leg is closed ONLY when green; never bank it at a loss
         continue;
      string comment = PositionGetString(POSITION_COMMENT);
      if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
         continue;
      datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
      if(!PositionAgeStale(openTime))
         continue;

      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;

      double loss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, volume);
      double lossPerLot = loss / volume;
      if(lossPerLot <= 0.0)
         continue;

      ArrayResize(candidates, n + 1);
      candidates[n].ticket = ticket;
      candidates[n].volume = volume;
      candidates[n].loss = loss;
      candidates[n].lossPerLot = lossPerLot;
      candidates[n].swap = PositionGetDouble(POSITION_SWAP);
      candidates[n].openTime = openTime;
      candidates[n].type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      candidates[n].onLargestExposureSide = false;
      n++;
     }
   return n;
  }

int CollectStaleTrendEntryCleanupCandidates(CleanupCandidate &candidates[])
  {
   ArrayResize(candidates, 0);
   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(IsFloatTicket(ticket)) // 0.0.44 NO-REALIZE: a floated leg is closed ONLY when green; never bank it at a loss
         continue;
      if(!IsTrendRescueComment(PositionGetString(POSITION_COMMENT)))
         continue;

      datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
      if(!PositionAgeStale(openTime))
         continue;

      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;

      double loss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, volume);
      double lossPerLot = loss / volume;
      if(lossPerLot <= 0.0)
         continue;

      ArrayResize(candidates, n + 1);
      candidates[n].ticket = ticket;
      candidates[n].volume = volume;
      candidates[n].loss = loss;
      candidates[n].lossPerLot = lossPerLot;
      candidates[n].swap = PositionGetDouble(POSITION_SWAP);
      candidates[n].openTime = openTime;
      candidates[n].type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      candidates[n].onLargestExposureSide = false;
      n++;
     }
   return n;
  }

bool StaleCleanupCandidateBetter(const CleanupCandidate &a,
                                 const CleanupCandidate &b)
  {
   if(a.openTime != b.openTime)
      return (a.openTime < b.openTime);
   if(MathAbs(a.lossPerLot - b.lossPerLot) > 1.0e-8)
      return (a.lossPerLot < b.lossPerLot);
   if(MathAbs(a.loss - b.loss) > 1.0e-8)
      return (a.loss < b.loss);
   return (a.ticket < b.ticket);
  }

void SortStaleCleanupCandidates(CleanupCandidate &candidates[])
  {
   int n = ArraySize(candidates);
   for(int i = 0; i < n - 1; i++)
     {
      int best = i;
      for(int j = i + 1; j < n; j++)
        {
         if(StaleCleanupCandidateBetter(candidates[j], candidates[best]))
            best = j;
        }
      if(best != i)
        {
         CleanupCandidate tmp = candidates[i];
         candidates[i] = candidates[best];
         candidates[best] = tmp;
        }
     }
  }

void AccumulateStaleTradeLossSeverity(CleanupCandidate &candidates[],
                                      double &largestStaleLoss,
                                      double &totalStaleLoss)
  {
   int n = ArraySize(candidates);
   for(int i = 0; i < n; i++)
     {
      double loss = MathMax(0.0, candidates[i].loss);
      if(loss <= 0.0)
         continue;
      if(loss > largestStaleLoss)
         largestStaleLoss = loss;
      totalStaleLoss = NormalizeAccountMoney(totalStaleLoss + loss);
     }
  }

int CountStaleTradeCleanupCandidates(int &oldCount,
                                     int &redCount,
                                     double &largestStaleLoss,
                                     double &totalStaleLoss)
  {
   largestStaleLoss = 0.0;
   totalStaleLoss = 0.0;

   CleanupCandidate oldLosers[];
   oldCount = CollectStaleOldGridCleanupCandidates(oldLosers);
   CleanupCandidate redTrendEntries[];
   redCount = CollectStaleTrendEntryCleanupCandidates(redTrendEntries);

   AccumulateStaleTradeLossSeverity(oldLosers, largestStaleLoss, totalStaleLoss);
   AccumulateStaleTradeLossSeverity(redTrendEntries, largestStaleLoss, totalStaleLoss);
   return oldCount + redCount;
  }

int CountStaleTradeCleanupCandidates(int &oldCount,
                                     int &redCount)
  {
   double largestStaleLoss = 0.0, totalStaleLoss = 0.0;
   return CountStaleTradeCleanupCandidates(oldCount, redCount,
                                           largestStaleLoss, totalStaleLoss);
  }

bool StaleTradeThresholdsBreached(int &staleCount,
                                  int &oldCount,
                                  int &redCount,
                                  double &largestStaleLoss,
                                  double &totalStaleLoss,
                                  double &equityDD,
                                  string &triggerReason)
  {
   staleCount = CountStaleTradeCleanupCandidates(oldCount, redCount,
                                                 largestStaleLoss, totalStaleLoss);
   equityDD = EquityFloatingDrawdown();
   triggerReason = "no_candidates";

   int triggerCount = InpStaleTradeTriggerCount;
   if(triggerCount < 0)
      triggerCount = 0;
   if(staleCount <= 0)
      return false;

   bool countTriggered = (triggerCount <= 0 || staleCount >= triggerCount);
   double largestTrigger = MoneyInput(InpStaleTradeLargestLossTriggerUSD);
   bool largestTriggered = (largestTrigger > 0.0 && largestStaleLoss >= largestTrigger);
   double totalTrigger = MoneyInput(InpStaleTradeTotalLossTriggerUSD);
   bool totalTriggered = (totalTrigger > 0.0 && totalStaleLoss >= totalTrigger);

   if(!countTriggered && !largestTriggered && !totalTriggered)
     {
      triggerReason = "below_trigger";
      return false;
     }

   if(equityDD < MoneyInput(InpStaleTradeMinEquityDDUSD))
     {
      triggerReason = "equity_dd_below_trigger";
      return false;
     }

   if(totalTriggered)
      triggerReason = "eligible_total_loss";
   else if(largestTriggered)
      triggerReason = "eligible_largest_loss";
   else
      triggerReason = "eligible_count";
   return true;
  }

bool StaleTradeThresholdsBreached(int &staleCount,
                                  int &oldCount,
                                  int &redCount,
                                  double &equityDD)
  {
   double largestStaleLoss = 0.0, totalStaleLoss = 0.0;
   string triggerReason = "";
   return StaleTradeThresholdsBreached(staleCount, oldCount, redCount,
                                       largestStaleLoss, totalStaleLoss,
                                       equityDD, triggerReason);
  }

void LogStaleTradeCleanupStatus(const string triggerReason,
                                const bool eligible,
                                const int staleCount,
                                const int oldCount,
                                const int redCount,
                                const double largestStaleLoss,
                                const double totalStaleLoss,
                                const double equityDD,
                                const double budget,
                                const double floorBudget,
                                const double perTickCap,
                                const double hourRemaining)
  {
   string state = (eligible ? "eligible" : "skipped");
   string key = StringFormat("stale-status|state=%s|reason=%s|staleCount=%d|oldCount=%d|redCount=%d",
                             state, triggerReason, staleCount, oldCount, redCount);
   if(!TrendRescueCleanupDiagLogAllowed(key, false))
      return;

   Log(0, StringFormat("Straddle: stale cleanup status: state=%s reason=%s staleCount=%d oldCount=%d redCount=%d largestStaleLoss=%.2f totalStaleLoss=%.2f equityDD=%.2f budget=%.2f floorBudget=%.2f floorBalance=%.2f perTickCap=%.2f hourRemaining=%.2f hourlySpent=%.2f triggerCount=%d largestTrigger=%.2f totalTrigger=%.2f minEquityDD=%.2f",
                       state, triggerReason, staleCount, oldCount, redCount,
                       largestStaleLoss, totalStaleLoss, equityDD, budget,
                       floorBudget, VoluntaryLossFloorBalance(MoneyInput(InpCleanupCostBufferUSD)),
                       perTickCap, hourRemaining, g_staleTradeLossHourSpent,
                       InpStaleTradeTriggerCount,
                       MoneyInput(InpStaleTradeLargestLossTriggerUSD),
                       MoneyInput(InpStaleTradeTotalLossTriggerUSD),
                       MoneyInput(InpStaleTradeMinEquityDDUSD)));
  }

double StaleTradeCleanupBudget(const string reason,
                               double &bookedBudget,
                               double &floorBudget,
                               double &perTickCap,
                               double &hourRemaining,
                               double &equityDD)
  {
   if(reason == "")
      Log(2, "Straddle: stale cleanup budget requested without a reason label");

   bookedBudget = 0.0;
   floorBudget = 0.0;
   perTickCap = MoneyInput(InpStaleTradeMaxLossPerTickUSD);
   hourRemaining = StaleTradeHourlyLossRemaining();
   equityDD = EquityFloatingDrawdown();
   if(perTickCap <= 0.0 || hourRemaining <= 0.0)
      return 0.0;

   double floorBuffer = MoneyInput(InpCleanupCostBufferUSD);
   floorBudget = VoluntaryLossBudget(floorBuffer);
   bookedBudget = floorBudget;
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   if(g_rescueAnchorTrusted && g_rescueAnchorBalance > 0.0)
      bookedBudget = MathMin(bookedBudget,
                             MathMax(0.0, balance - g_rescueAnchorBalance -
                                           MoneyInput(InpProfitReserveUSD) - floorBuffer));
   else if(g_cycleStartTrusted)
      bookedBudget = MathMin(bookedBudget,
                             MathMax(0.0, CycleRealized() -
                                           MoneyInput(InpProfitReserveUSD) - floorBuffer));

   double budget = MathMin(bookedBudget, MathMin(floorBudget, MathMin(perTickCap, hourRemaining)));
   return MathMax(0.0, NormalizeAccountMoney(budget));
  }

bool TryStaleTradeCloseLane(CleanupCandidate &candidates[],
                            const bool isTrendEntry,
                            const double budget,
                            const string lane,
                            bool &belowMinLot,
                            bool &voluntaryFloor,
                            bool &closeFailed,
                            double &spentLoss)
  {
   SortStaleCleanupCandidates(candidates);
   int n = ArraySize(candidates);
   double floorBuffer = MoneyInput(InpCleanupCostBufferUSD);
   string belowMinGuardKey = TrendRescueBelowMinLotStateKey("stale-" + lane, candidates,
                                                            budget, floorBuffer);
   if(TrendRescueBelowMinLotGuardActive("stale-" + lane, belowMinGuardKey))
     {
      belowMinLot = true;
      LogTrendRescueCleanupDiag("stale", "below_min_lot_guard",
                                StringFormat("lane=%s budget=%.2f candidates=%d",
                                             lane, budget, n));
      return false;
     }

   for(int i = 0; i < n; i++)
     {
      ulong ticket = candidates[i].ticket;
      if(ticket == 0 || !PositionSelectByTicket(ticket))
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;

      string comment = PositionGetString(POSITION_COMMENT);
      if(isTrendEntry)
        {
         if(!IsTrendRescueComment(comment))
            continue;
        }
      else
        {
         if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
            continue;
        }

      datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
      if(!PositionAgeStale(openTime))
         continue;
      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;
      double lossPerLot = MathAbs(positionProfitAndSwap) / volume;
      if(lossPerLot <= 0.0)
         continue;

      double floorBudget = 0.0;
      if(!VoluntaryLossCloseAllowed(StringFormat("stale-cleanup-voluntary_floor-%s", lane),
                                    ticket, 0.0, floorBuffer, floorBudget))
        {
         voluntaryFloor = true;
         continue;
        }
      double closeBudget = MathMin(budget, floorBudget);
      if(closeBudget <= 0.0)
        {
         voluntaryFloor = true;
         continue;
        }

      double budgetLossPerLot = lossPerLot + EstimatedCloseCost(1.0, ticket);
      double closeLots = CleanupChunkLots(volume, budgetLossPerLot, closeBudget);
      if(closeLots <= 0.0)
        {
         belowMinLot = true;
         continue;
        }

      double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap,
                                                             volume, closeLots);
      if(estimatedLoss > closeBudget + 1.0e-6)
         continue;
      if(!VoluntaryLossCloseAllowed(StringFormat("stale-cleanup-voluntary_floor-%s", lane),
                                    ticket, estimatedLoss, floorBuffer, floorBudget))
        {
         voluntaryFloor = true;
         continue;
        }
      closeBudget = MathMin(closeBudget, floorBudget);
      if(estimatedLoss > closeBudget + 1.0e-6)
        {
         voluntaryFloor = true;
         continue;
        }

      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
      double beforeBudget = closeBudget;
      if(CloseTrendRescuePairLoser(ticket, isTrendEntry, closeLots, estimatedLoss, closeBudget))
        {
         spentLoss = estimatedLoss;
         RecordStaleTradeLossSpend(estimatedLoss);
         TrendRescueBelowMinLotGuardClear("stale-" + lane);
         LogAlways(StringFormat("Straddle: stale cleanup action=partial lane=%s ticket #%I64u type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f ageMinutes=%d hourlySpent=%.2f hourlyCap=%.2f",
                                lane, ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                closeLots, estimatedLoss, estimatedCloseCost,
                                beforeBudget,
                             (int)((TimeCurrent() - openTime) / 60),
                             g_staleTradeLossHourSpent,
                             MoneyInput(InpStaleTradeMaxLossPerHourUSD)));
         return true;
        }

      closeFailed = true;
      LogTrendRescueCleanupDiag("stale", "close_failed",
                                StringFormat("lane=%s ticket #%I64u closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f",
                                             lane, ticket, closeLots, estimatedLoss,
                                             estimatedCloseCost, beforeBudget));
     }

   if(belowMinLot)
      TrendRescueBelowMinLotGuardRecord("stale-" + lane, belowMinGuardKey);
   else
      TrendRescueBelowMinLotGuardClear("stale-" + lane);

   return false;
  }

bool TryStaleTradeCleanup(const string reason)
  {
   if(!RealizedLossClosesAllowed())
      return false;
   if(!g_trendRescueActive || !InpUseStaleTradeCleanup)
      return false;
   if(InpStaleTradeMaxActionsPerTick <= 0)
     {
      LogTrendRescueCleanupDiag("stale", "max_actions",
                                StringFormat("maxActions=%d", InpStaleTradeMaxActionsPerTick));
      return false;
     }
   int oldCount = 0, redCount = 0, staleCount = 0;
   double largestStaleLoss = 0.0, totalStaleLoss = 0.0, equityDD = 0.0;
   string triggerReason = "";
   bool eligible = StaleTradeThresholdsBreached(staleCount, oldCount, redCount,
                                                largestStaleLoss, totalStaleLoss,
                                                equityDD, triggerReason);
   double statusBookedBudget = 0.0, statusFloorBudget = 0.0, statusPerTickCap = 0.0;
   double statusHourRemaining = 0.0, statusEquityDD = 0.0;
   double statusBudget = StaleTradeCleanupBudget(reason, statusBookedBudget,
                                                 statusFloorBudget, statusPerTickCap,
                                                 statusHourRemaining, statusEquityDD);
   LogStaleTradeCleanupStatus(triggerReason, eligible, staleCount, oldCount, redCount,
                              largestStaleLoss, totalStaleLoss, equityDD, statusBudget,
                              statusFloorBudget, statusPerTickCap, statusHourRemaining);
   if(!eligible)
     {
      if(staleCount <= 0)
         LogTrendRescueCleanupDiag("stale", "no_candidates");
      else if(triggerReason == "below_trigger")
         LogTrendRescueCleanupDiag("stale", "below_trigger",
                                   StringFormat("candidates=%d oldCount=%d redCount=%d largestStaleLoss=%.2f totalStaleLoss=%.2f trigger=%d largestTrigger=%.2f totalTrigger=%.2f equityDD=%.2f",
                                                staleCount, oldCount, redCount,
                                                largestStaleLoss, totalStaleLoss,
                                                InpStaleTradeTriggerCount,
                                                MoneyInput(InpStaleTradeLargestLossTriggerUSD),
                                                MoneyInput(InpStaleTradeTotalLossTriggerUSD),
                                                equityDD));
      else
         LogTrendRescueCleanupDiag("stale", "equity_dd_below_trigger",
                                   StringFormat("candidates=%d oldCount=%d redCount=%d largestStaleLoss=%.2f totalStaleLoss=%.2f equityDD=%.2f minEquityDD=%.2f",
                                                staleCount, oldCount, redCount,
                                                largestStaleLoss, totalStaleLoss,
                                                equityDD,
                                                MoneyInput(InpStaleTradeMinEquityDDUSD)));
      return false;
     }

   bool closedAny = false;
   int actions = 0;
   double remainingTickBudget = MoneyInput(InpStaleTradeMaxLossPerTickUSD);
   while(actions < InpStaleTradeMaxActionsPerTick)
     {
      CleanupCandidate oldLosers[];
      oldCount = CollectStaleOldGridCleanupCandidates(oldLosers);
      CleanupCandidate redTrendEntries[];
      redCount = CollectStaleTrendEntryCleanupCandidates(redTrendEntries);
      staleCount = oldCount + redCount;
      if(staleCount <= 0)
        {
         LogTrendRescueCleanupDiag("stale", "no_candidates");
         break;
        }

      double bookedBudget = 0.0, floorBudget = 0.0, perTickCap = 0.0, hourRemaining = 0.0;
      double budgetEquityDD = 0.0;
      double currentBudget = StaleTradeCleanupBudget(reason, bookedBudget, floorBudget,
                                                     perTickCap, hourRemaining, budgetEquityDD);
      double budget = MathMin(currentBudget, remainingTickBudget);
      LogTrendRescueCleanupDiag("stale", "budget",
                                StringFormat("reason=%s budget=%.2f currentBudget=%.2f bookedBudget=%.2f floorBudget=%.2f remainingTickBudget=%.2f perTickCap=%.2f hourRemaining=%.2f hourlySpent=%.2f equityDD=%.2f minEquityDD=%.2f candidates=%d oldCount=%d redCount=%d actions=%d/%d",
                                             reason, budget, currentBudget, bookedBudget,
                                             floorBudget, remainingTickBudget, perTickCap,
                                             hourRemaining, g_staleTradeLossHourSpent,
                                             budgetEquityDD, MoneyInput(InpStaleTradeMinEquityDDUSD),
                                             staleCount, oldCount, redCount, actions,
                                             InpStaleTradeMaxActionsPerTick));
      if(budget <= 0.0)
        {
         string why = (hourRemaining <= 0.0 ? "hourly_cap" : "no_budget");
         LogTrendRescueCleanupDiag("stale", why,
                                   StringFormat("budget=%.2f bookedBudget=%.2f floorBudget=%.2f perTickCap=%.2f hourRemaining=%.2f hourlySpent=%.2f candidates=%d",
                                                budget, bookedBudget, floorBudget,
                                                perTickCap, hourRemaining,
                                                g_staleTradeLossHourSpent, staleCount));
         break;
        }

      bool belowMinLot = false;
      bool voluntaryFloor = false;
      bool closeFailed = false;
      double spentLoss = 0.0;
      if(oldCount > 0 &&
         TryStaleTradeCloseLane(oldLosers, false, budget, "old-grid",
                                belowMinLot, voluntaryFloor, closeFailed, spentLoss))
        {
         actions++;
         closedAny = true;
         remainingTickBudget = MathMax(0.0, NormalizeAccountMoney(remainingTickBudget - spentLoss));
         if(remainingTickBudget <= 0.0)
           {
            LogTrendRescueCleanupDiag("stale", "tick_budget_exhausted",
                                      StringFormat("remainingTickBudget=%.2f spentLoss=%.2f actions=%d candidates=%d lane=old-grid",
                                                   remainingTickBudget, spentLoss, actions,
                                                   staleCount));
            break;
           }
         continue;
        }

      spentLoss = 0.0;
      if(redCount > 0 &&
         TryStaleTradeCloseLane(redTrendEntries, true, budget, "trend-entry",
                                belowMinLot, voluntaryFloor, closeFailed, spentLoss))
        {
         actions++;
         closedAny = true;
         remainingTickBudget = MathMax(0.0, NormalizeAccountMoney(remainingTickBudget - spentLoss));
         if(remainingTickBudget <= 0.0)
           {
            LogTrendRescueCleanupDiag("stale", "tick_budget_exhausted",
                                      StringFormat("remainingTickBudget=%.2f spentLoss=%.2f actions=%d candidates=%d lane=trend-entry",
                                                   remainingTickBudget, spentLoss, actions,
                                                   staleCount));
            break;
           }
         continue;
        }

      if(voluntaryFloor)
         LogTrendRescueCleanupDiag("stale", "voluntary_floor",
                                   StringFormat("budget=%.2f oldCount=%d redCount=%d candidates=%d",
                                                budget, oldCount, redCount, staleCount));
      else if(belowMinLot)
         LogTrendRescueCleanupDiag("stale", "below_min_lot",
                                   StringFormat("budget=%.2f oldCount=%d redCount=%d candidates=%d",
                                                budget, oldCount, redCount, staleCount));
      else if(closeFailed)
         LogTrendRescueCleanupDiag("stale", "close_failed",
                                   StringFormat("budget=%.2f oldCount=%d redCount=%d candidates=%d",
                                                budget, oldCount, redCount, staleCount));
      else
         LogTrendRescueCleanupDiag("stale", "no_fundable_action",
                                   StringFormat("budget=%.2f oldCount=%d redCount=%d candidates=%d",
                                                budget, oldCount, redCount, staleCount));
      break;
     }

   if(actions >= InpStaleTradeMaxActionsPerTick)
      LogTrendRescueCleanupDiag("stale", "max_actions",
                                StringFormat("actions=%d maxActions=%d",
                                             actions, InpStaleTradeMaxActionsPerTick));

   return closedAny;
  }

bool StaleTradeCleanupLaneActionable(CleanupCandidate &candidates[],
                                     const bool isTrendEntry,
                                     const double budget,
                                     const string lane,
                                     bool &belowMinLot,
                                     bool &voluntaryFloor,
                                     bool &noBudget)
  {
   SortStaleCleanupCandidates(candidates);
   int n = ArraySize(candidates);
   double floorBuffer = MoneyInput(InpCleanupCostBufferUSD);
   for(int i = 0; i < n; i++)
     {
      ulong ticket = candidates[i].ticket;
      if(ticket == 0 || !PositionSelectByTicket(ticket))
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;

      string comment = PositionGetString(POSITION_COMMENT);
      if(isTrendEntry)
        {
         if(!IsTrendRescueComment(comment))
            continue;
        }
      else
        {
         if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
            continue;
        }

      datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
      if(!PositionAgeStale(openTime))
         continue;
      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;
      double lossPerLot = MathAbs(positionProfitAndSwap) / volume;
      if(lossPerLot <= 0.0)
         continue;

      double floorBudget = VoluntaryLossBudget(floorBuffer);
      double closeBudget = MathMin(budget, floorBudget);
      if(closeBudget <= 0.0)
        {
         if(floorBudget <= 0.0)
            voluntaryFloor = true;
         else
            noBudget = true;
         continue;
        }

      double budgetLossPerLot = lossPerLot + EstimatedCloseCost(1.0, ticket);
      double closeLots = CleanupChunkLots(volume, budgetLossPerLot, closeBudget);
      if(closeLots <= 0.0)
        {
         belowMinLot = true;
         continue;
        }

      double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap,
                                                             volume, closeLots);
      if(estimatedLoss <= closeBudget + 1.0e-6)
         return true;
      voluntaryFloor = true;
     }

   if(n <= 0)
      noBudget = (budget <= 0.0);
   return false;
  }

bool StaleTradeCleanupActionable(const string reason,
                                 int &staleCount,
                                 double &equityDD,
                                 string &actionabilityReason,
                                 bool &cleanupTriggered)
  {
   staleCount = 0;
   equityDD = EquityFloatingDrawdown();
   actionabilityReason = "";
   cleanupTriggered = false;
   if(!InpUseStaleTradeCleanup || InpStaleTradeMaxActionsPerTick <= 0)
     {
      actionabilityReason = "disabled";
      return false;
     }

   int oldCount = 0, redCount = 0;
   double largestStaleLoss = 0.0, totalStaleLoss = 0.0;
   string triggerReason = "";
   bool eligible = StaleTradeThresholdsBreached(staleCount, oldCount, redCount,
                                                largestStaleLoss, totalStaleLoss,
                                                equityDD, triggerReason);
   if(!eligible)
     {
      actionabilityReason = triggerReason;
      return false;
     }
   cleanupTriggered = true;

   double bookedBudget = 0.0, floorBudget = 0.0, perTickCap = 0.0, hourRemaining = 0.0;
   double budgetEquityDD = 0.0;
   double budget = StaleTradeCleanupBudget(reason, bookedBudget, floorBudget,
                                           perTickCap, hourRemaining, budgetEquityDD);
   if(budget <= 0.0)
     {
      if(hourRemaining <= 0.0)
         actionabilityReason = "hourly_cap";
      else if(perTickCap <= 0.0)
         actionabilityReason = "no_tick_budget";
      else if(floorBudget <= 0.0)
         actionabilityReason = "voluntary_floor";
      else
         actionabilityReason = "no_budget";
      return false;
     }

   CleanupCandidate oldLosers[];
   oldCount = CollectStaleOldGridCleanupCandidates(oldLosers);
   CleanupCandidate redTrendEntries[];
   redCount = CollectStaleTrendEntryCleanupCandidates(redTrendEntries);
   staleCount = oldCount + redCount;
   if(staleCount <= 0)
     {
      actionabilityReason = "no_candidates";
      return false;
     }

   bool belowMinLot = false;
   bool voluntaryFloor = false;
   bool noBudget = false;
   if(oldCount > 0 &&
      StaleTradeCleanupLaneActionable(oldLosers, false, budget, "old-grid",
                                      belowMinLot, voluntaryFloor, noBudget))
     {
      actionabilityReason = "actionable_old_grid";
      return true;
     }
   if(redCount > 0 &&
      StaleTradeCleanupLaneActionable(redTrendEntries, true, budget, "trend-entry",
                                      belowMinLot, voluntaryFloor, noBudget))
     {
      actionabilityReason = "actionable_trend_entry";
      return true;
     }

   if(voluntaryFloor)
      actionabilityReason = "voluntary_floor";
   else if(belowMinLot)
      actionabilityReason = "below_min_lot";
   else if(noBudget)
      actionabilityReason = "no_budget";
   else
      actionabilityReason = "no_fundable_action";
   return false;
  }

bool StaleTradeBackpressureDecision(int &staleCount,
                                    double &equityDD,
                                    string &actionabilityReason,
                                    bool &cleanupTriggered)
  {
   return StaleTradeCleanupActionable("stale-backpressure",
                                      staleCount,
                                      equityDD,
                                      actionabilityReason,
                                      cleanupTriggered);
  }

void LogStaleBackpressureBypassedNoActionableCleanup(const int staleCount,
                                                     const double equityDD,
                                                     const string actionabilityReason,
                                                     const double coverageGap)
  {
   string key = StringFormat("stale-backpressure-bypass|reason=%s|count=%d|dd=%s|gap=%s",
                             actionabilityReason,
                             staleCount,
                             MoneyThrottleBucketKey(equityDD),
                             MoneyThrottleBucketKey(coverageGap));
   if(!TrendRescueCleanupDiagLogAllowed(key, false))
      return;

   Log(1, StringFormat("Straddle: trend rescue stale backpressure bypassed: reason=stale_backpressure_bypassed_no_actionable_cleanup actionability=%s staleResiduals=%d trigger=%d equityDD=%.2f minEquityDD=%.2f coverageGap=%.2f",
                       actionabilityReason,
                       staleCount,
                       InpStaleTradeTriggerCount,
                       equityDD,
                       MoneyInput(InpStaleTradeMinEquityDDUSD),
                       coverageGap));
  }

double StuckRecoveryCleanupBudget(const string reason,
                                  double &floorBalance,
                                  double &floorEquity,
                                  double &balanceCushion,
                                  double &equityCushion)
  {
   if(reason == "")
      Log(2, "Straddle: stuck cleanup budget requested without a reason label");

   floorBalance = 0.0;
   floorEquity = 0.0;
   balanceCushion = 0.0;
   equityCushion = 0.0;

   if(!g_trendRescueActive)
      return 0.0;
   if(CountMyPositions() <= 0)
      return 0.0;
   if(!g_rescueAnchorTrusted || g_rescueAnchorBalance <= 0.0)
      return 0.0;

   double floorBuffer = MoneyInput(InpStuckRecoveryBalanceCushionUSD);
   floorBalance = VoluntaryLossFloorBalance(floorBuffer);
   floorEquity = floorBalance + MoneyInput(InpStuckRecoveryMinEquityBufferUSD);

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   balanceCushion = MathMax(0.0, balance - floorBalance);
   equityCushion = MathMax(0.0, equity - floorEquity);

   double confirmedCushion = MathMin(balanceCushion, equityCushion);
   double spendShare = MathMin(1.0, MathMax(0.0, InpStuckRecoverySpendShare));
   double budget = confirmedCushion * spendShare;
   double maxSpend = MoneyInput(InpStuckRecoveryMaxSpendUSD);
   if(maxSpend <= 0.0)
      return 0.0;
   budget = MathMin(budget, maxSpend);
   return MathMax(0.0, NormalizeAccountMoney(budget));
  }

bool TryStuckRecoveryCloseLane(CleanupCandidate &candidates[],
                               const bool isTrendEntry,
                               const double budget,
                               const double coverageGap,
                               const string reason,
                               const string lane,
                               bool &belowMinLot,
                               bool &voluntaryFloor,
                               bool &closeFailed,
                               double &spentLoss)
  {
   if(!RealizedLossClosesAllowed())
      return false;
   SortTrendRescueCleanupCandidates(candidates);
   double floorBuffer = MoneyInput(InpStuckRecoveryBalanceCushionUSD);

   int n = ArraySize(candidates);
   string guardLane = "stuck-" + lane;
   string belowMinGuardKey = TrendRescueBelowMinLotStateKey(guardLane, candidates, budget, floorBuffer,
                                                             "|coverage=" + TrendRescueMoneyStateKey(coverageGap));
   if(TrendRescueBelowMinLotGuardActive(guardLane, belowMinGuardKey))
     {
      belowMinLot = true;
      LogTrendRescueCleanupDiag("stuck", "below_min_lot_guard",
                                StringFormat("lane=%s budget=%.2f coverageGap=%.2f candidates=%d",
                                             lane, budget, coverageGap, n));
      return false;
     }

   for(int i = 0; i < n; i++)
     {
      ulong ticket = candidates[i].ticket;
      if(ticket == 0 || !PositionSelectByTicket(ticket))
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;

      string comment = PositionGetString(POSITION_COMMENT);
      if(isTrendEntry)
        {
         if(!IsTrendRescueComment(comment))
            continue;
        }
      else
        {
         if(IsTrendRescueComment(comment) || IsRescueHedgeComment(comment) || IsAveragingComment(comment)) // 0.0.39: never realize loss on AVG add
            continue;
        }

      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      double positionProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(positionProfitAndSwap >= 0.0)
         continue;

      double lossPerLot = MathAbs(positionProfitAndSwap) / volume;
      if(lossPerLot <= 0.0)
         continue;

      double floorBudget = VoluntaryLossBudget(floorBuffer);
      double closeBudget = MathMin(budget, floorBudget);
      if(closeBudget <= 0.0)
        {
         voluntaryFloor = true;
         continue;
        }

      double budgetLossPerLot = lossPerLot + EstimatedCloseCost(1.0, ticket);
      double closeLots = CleanupChunkLots(volume, budgetLossPerLot, closeBudget);
      if(closeLots <= 0.0)
        {
         belowMinLot = true;
         continue;
        }

      double estimatedLoss = TrendRescueCleanupEstimatedLoss(ticket, positionProfitAndSwap, volume, closeLots);
      if(estimatedLoss > closeBudget + 1.0e-6)
         continue;

      double allowedBudget = 0.0;
      if(!VoluntaryLossCloseAllowed(StringFormat("stuck-cleanup-voluntary_floor-%s-%s", lane, reason),
                                    ticket, estimatedLoss, floorBuffer, allowedBudget))
        {
         voluntaryFloor = true;
         continue;
        }
      closeBudget = MathMin(closeBudget, allowedBudget);
      if(estimatedLoss > closeBudget + 1.0e-6)
        {
         voluntaryFloor = true;
         continue;
        }

      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      double estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
      double beforeBudget = closeBudget;
      bool closed = CloseTrendRescuePairLoser(ticket, isTrendEntry, closeLots, estimatedLoss, closeBudget);
      if(closed)
        {
         spentLoss = estimatedLoss;
         double floorBalance = 0.0, floorEquity = 0.0, balanceCushion = 0.0, equityCushion = 0.0;
         double afterBudget = StuckRecoveryCleanupBudget(reason, floorBalance, floorEquity,
                                                         balanceCushion, equityCushion);
         estimatedCloseCost = EstimatedCloseCost(closeLots, ticket);
          LogAlways(StringFormat("Straddle: stuck cleanup action=partial lane=%s ticket #%I64u type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f coverageGap=%.2f floorBalance=%.2f floorEquity=%.2f balanceCushion=%.2f equityCushion=%.2f reason=%s",
                                 lane, ticket, (type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                closeLots, estimatedLoss, estimatedCloseCost,
                                beforeBudget, afterBudget, coverageGap, floorBalance, floorEquity,
                                balanceCushion, equityCushion, reason));
         return true;
        }

      closeFailed = true;
      LogTrendRescueCleanupDiag("stuck", "close_failed",
                                 StringFormat("lane=%s ticket #%I64u closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f coverageGap=%.2f",
                                              lane, ticket, closeLots, estimatedLoss, estimatedCloseCost,
                                              beforeBudget, coverageGap));
     }

   if(belowMinLot)
      TrendRescueBelowMinLotGuardRecord(guardLane, belowMinGuardKey);
   else
      TrendRescueBelowMinLotGuardClear(guardLane);

   return false;
  }

bool TryTrendRescueStuckRecoveryCleanup(const string reason,
                                        const double coverageGap,
                                        const bool normalCleanupProgress)
  {
   if(!g_trendRescueActive || !InpUseStuckRecoveryCleanup)
      return false;
   if(normalCleanupProgress)
      return false;
   if(InpStuckRecoveryMaxActionsPerTick <= 0)
     {
      LogTrendRescueCleanupDiag("stuck", "max_actions",
                                StringFormat("maxActions=%d", InpStuckRecoveryMaxActionsPerTick));
      return false;
     }
   double gapThreshold = MoneyInput(InpStuckRecoveryGapUSD);
   if(coverageGap < gapThreshold)
      return false;
   bool closedAny = false;
   int actions = 0;
   double currentCoverageGap = coverageGap;
   double initialFloorBalance = 0.0, initialFloorEquity = 0.0, initialBalanceCushion = 0.0, initialEquityCushion = 0.0;
   double remainingTickBudget = StuckRecoveryCleanupBudget(reason, initialFloorBalance, initialFloorEquity,
                                                           initialBalanceCushion, initialEquityCushion);
   while(actions < InpStuckRecoveryMaxActionsPerTick)
     {
      if(remainingTickBudget <= 0.0)
        {
         LogTrendRescueCleanupDiag("stuck", "tick_budget_exhausted",
                                   StringFormat("remainingTickBudget=%.2f actions=%d coverageGap=%.2f maxSpend=%.2f floorBalance=%.2f floorEquity=%.2f balanceCushion=%.2f equityCushion=%.2f",
                                                remainingTickBudget, actions, currentCoverageGap,
                                                MoneyInput(InpStuckRecoveryMaxSpendUSD),
                                                initialFloorBalance, initialFloorEquity,
                                                initialBalanceCushion, initialEquityCushion));
         break;
        }

      if(currentCoverageGap < gapThreshold)
        {
         LogTrendRescueCleanupDiag("stuck", "gap_recovered",
                                   StringFormat("coverageGap=%.2f threshold=%.2f actions=%d",
                                                currentCoverageGap, gapThreshold, actions));
         break;
        }

      CleanupCandidate oldLosers[];
      int oldCount = CollectTrendRescueCleanupCandidates(oldLosers);
      CleanupCandidate redTrendEntries[];
      int redCount = CollectTrendRescueEntryCleanupCandidates(redTrendEntries, false, IsTrendRescueBuy());
      if(oldCount <= 0 && redCount <= 0)
        {
         LogTrendRescueCleanupDiag("stuck", "no_candidates",
                                   StringFormat("coverageGap=%.2f oldCandidates=%d redCandidates=%d",
                                                currentCoverageGap, oldCount, redCount));
         break;
        }

      double floorBalance = 0.0, floorEquity = 0.0, balanceCushion = 0.0, equityCushion = 0.0;
      double currentSafetyBudget = StuckRecoveryCleanupBudget(reason, floorBalance, floorEquity,
                                                              balanceCushion, equityCushion);
      double budget = MathMin(currentSafetyBudget, remainingTickBudget);
      LogTrendRescueCleanupDiag("stuck", "activated",
                                StringFormat("reason=%s coverageGap=%.2f threshold=%.2f oldCandidates=%d redCandidates=%d actions=%d/%d",
                                             reason, currentCoverageGap, gapThreshold, oldCount, redCount,
                                             actions, InpStuckRecoveryMaxActionsPerTick));
      LogTrendRescueCleanupDiag("stuck", "budget",
                                StringFormat("reason=%s budget=%.2f currentSafetyBudget=%.2f remainingTickBudget=%.2f maxSpend=%.2f spendShare=%.2f floorBalance=%.2f floorEquity=%.2f balanceCushion=%.2f equityCushion=%.2f",
                                             reason, budget, currentSafetyBudget, remainingTickBudget,
                                             MoneyInput(InpStuckRecoveryMaxSpendUSD),
                                             InpStuckRecoverySpendShare, floorBalance, floorEquity,
                                             balanceCushion, equityCushion));

      if(budget <= 0.0)
        {
         LogTrendRescueCleanupDiag("stuck", "no_budget",
                                   StringFormat("budget=%.2f coverageGap=%.2f floorBalance=%.2f floorEquity=%.2f balanceCushion=%.2f equityCushion=%.2f oldCandidates=%d redCandidates=%d",
                                                budget, currentCoverageGap, floorBalance, floorEquity,
                                                balanceCushion, equityCushion, oldCount, redCount));
         break;
        }

      bool belowMinLot = false;
      bool voluntaryFloor = false;
      bool closeFailed = false;
      double spentLoss = 0.0;
      if(oldCount > 0 &&
         TryStuckRecoveryCloseLane(oldLosers, false, budget, currentCoverageGap, reason,
                                   "old-grid", belowMinLot, voluntaryFloor, closeFailed, spentLoss))
        {
         actions++;
         closedAny = true;
         remainingTickBudget = MathMax(0.0, NormalizeAccountMoney(remainingTickBudget - spentLoss));
         currentCoverageGap = TrendRescueCoverageGap();
         if(remainingTickBudget <= 0.0)
           {
            LogTrendRescueCleanupDiag("stuck", "tick_budget_exhausted",
                                      StringFormat("remainingTickBudget=%.2f spentLoss=%.2f actions=%d coverageGap=%.2f maxSpend=%.2f lane=old-grid",
                                                   remainingTickBudget, spentLoss, actions, currentCoverageGap,
                                                   MoneyInput(InpStuckRecoveryMaxSpendUSD)));
            break;
           }
         continue;
        }

      spentLoss = 0.0;
      if(redCount > 0 &&
         TryStuckRecoveryCloseLane(redTrendEntries, true, budget, currentCoverageGap, reason,
                                   "trend-entry", belowMinLot, voluntaryFloor, closeFailed, spentLoss))
        {
         actions++;
         closedAny = true;
         remainingTickBudget = MathMax(0.0, NormalizeAccountMoney(remainingTickBudget - spentLoss));
         currentCoverageGap = TrendRescueCoverageGap();
         if(remainingTickBudget <= 0.0)
           {
            LogTrendRescueCleanupDiag("stuck", "tick_budget_exhausted",
                                      StringFormat("remainingTickBudget=%.2f spentLoss=%.2f actions=%d coverageGap=%.2f maxSpend=%.2f lane=trend-entry",
                                                   remainingTickBudget, spentLoss, actions, currentCoverageGap,
                                                   MoneyInput(InpStuckRecoveryMaxSpendUSD)));
            break;
           }
         continue;
        }

      if(voluntaryFloor)
        {
         LogTrendRescueCleanupDiag("stuck", "voluntary_floor",
                                   StringFormat("budget=%.2f coverageGap=%.2f oldCandidates=%d redCandidates=%d",
                                                budget, currentCoverageGap, oldCount, redCount));
        }
      else if(belowMinLot)
        {
         LogTrendRescueCleanupDiag("stuck", "below_min_lot",
                                   StringFormat("budget=%.2f coverageGap=%.2f oldCandidates=%d redCandidates=%d",
                                                budget, currentCoverageGap, oldCount, redCount));
        }
      else if(closeFailed)
        {
         LogTrendRescueCleanupDiag("stuck", "close_failed",
                                   StringFormat("budget=%.2f coverageGap=%.2f oldCandidates=%d redCandidates=%d",
                                                budget, currentCoverageGap, oldCount, redCount));
        }
      else if(redCount <= 0)
        {
         LogTrendRescueCleanupDiag("stuck", "no_candidates",
                                   StringFormat("lane=trend-entry budget=%.2f coverageGap=%.2f oldCandidates=%d redCandidates=%d",
                                                budget, currentCoverageGap, oldCount, redCount));
        }
      else
        {
         LogTrendRescueCleanupDiag("stuck", "no_fundable_action",
                                   StringFormat("budget=%.2f coverageGap=%.2f oldCandidates=%d redCandidates=%d",
                                                budget, currentCoverageGap, oldCount, redCount));
        }
      break;
     }

   if(actions >= InpStuckRecoveryMaxActionsPerTick)
      LogTrendRescueCleanupDiag("stuck", "max_actions",
                                StringFormat("actions=%d maxActions=%d",
                                             actions, InpStuckRecoveryMaxActionsPerTick));

   return closedAny;
  }

bool TryTrendRescuePairFundedCleanup(const string reason)
  {
   if(!RealizedLossClosesAllowed())
      return false;
   if(!g_trendRescueActive || !InpUseTrendRescuePairFundedCleanup)
      return false;
   if(InpPairCleanupMaxActionsPerTick <= 0)
     {
      Log(2, StringFormat("Straddle: trend rescue pair cleanup skipped: reason=max_actions maxActions=%d",
                          InpPairCleanupMaxActionsPerTick));
      return false;
     }
   bool closedAny = false;
   int actions = 0;
   while(actions < InpPairCleanupMaxActionsPerTick)
     {
      if(OrphanedPairProfitReservePending(reason))
        {
         if(TryTrendRescuePairOrphanedReserveCleanup(reason, actions, closedAny))
            continue;
         LogTrendRescueCleanupDiag("pair", "orphaned_reserve_pending",
                                   StringFormat("orphanedPairProfitReserve=%.2f reserveBudget=%.2f actions=%d/%d",
                                                g_deferredPairProfitReserve,
                                                TrendRescuePairOrphanedReserveBudget(reason),
                                                actions, InpPairCleanupMaxActionsPerTick));
         break;
        }

      double balanceBefore = AccountInfoDouble(ACCOUNT_BALANCE);
      double budgetBefore = TrendRescuePairCleanupBudget(reason);
      TrendRescueProfitCandidate profits[];
      int profitCount = CollectTrendRescuePairProfitCandidates(profits);
      if(profitCount <= 0)
        {
         TrendRescueBelowMinLotGuardClear("pair");
         Log(2, StringFormat("Straddle: trend rescue pair cleanup skipped: reason=no_profit_candidates budgetBefore=%.2f minProfit=%.2f buffer=%.2f",
                              budgetBefore, MoneyInput(InpPairCleanupMinProfitUSD), MoneyInput(InpPairCleanupBufferUSD)));
         LogTrendRescueCleanupDiag("pair", "no_profit_candidates",
                                   StringFormat("budgetBefore=%.2f", budgetBefore));
         break;
        }

      SortTrendRescuePairProfitCandidates(profits);
      string belowMinGuardKey = TrendRescuePairBelowMinLotStateKey(profits, budgetBefore, balanceBefore);
      if(TrendRescueBelowMinLotGuardActive("pair", belowMinGuardKey))
        {
         LogTrendRescueCleanupDiag("pair", "below_min_lot_guard",
                                   StringFormat("budgetBefore=%.2f profitCandidates=%d", budgetBefore, profitCount));
         break;
        }

      int selectedProfitIndex = -1;
      bool selectedLoserIsTrendEntry = false;
      ulong selectedLoserTicket = 0;
      double selectedCloseLots = 0.0;
      double selectedEstimatedLoss = 0.0;
      double selectedEstimatedCloseCost = 0.0;
      double selectedEstimatedBudget = 0.0;
      double selectedReservedLossBudget = 0.0;
      bool pairBelowMinLot = false;

      for(int i = 0; i < profitCount; i++)
        {
         double balanceAfterCandidateProfit = balanceBefore + profits[i].profit - profits[i].estimatedCloseCost;
         double reservedPairLossBudget = VoluntaryLossBudgetAtBalance(balanceAfterCandidateProfit,
                                                                      MoneyInput(InpPairCleanupBufferUSD));
         selectedEstimatedBudget = MathMin(MathMax(0.0, budgetBefore + profits[i].profit - profits[i].estimatedCloseCost),
                                           reservedPairLossBudget);
          if(selectedEstimatedBudget <= 0.0)
             continue;
          if(SelectTrendRescuePairLoser(selectedEstimatedBudget, selectedLoserIsTrendEntry,
                                        selectedLoserTicket, selectedCloseLots,
                                        selectedEstimatedLoss, selectedEstimatedCloseCost,
                                        pairBelowMinLot))
             {
             double preflightBudget = 0.0;
             if(VoluntaryLossCloseAllowedAtBalance(StringFormat("pair-cleanup-preflight-voluntary_loss_floor-%s", reason),
                                                   selectedLoserTicket,
                                                   balanceAfterCandidateProfit,
                                                   selectedEstimatedLoss,
                                                   MoneyInput(InpPairCleanupBufferUSD),
                                                   preflightBudget))
               {
                selectedReservedLossBudget = MathMin(selectedEstimatedBudget, preflightBudget);
                selectedProfitIndex = i;
                break;
               }
            }
         }

      if(selectedProfitIndex < 0)
        {
         if(pairBelowMinLot)
           {
            TrendRescueBelowMinLotGuardRecord("pair", belowMinGuardKey);
            Log(2, StringFormat("Straddle: trend rescue pair cleanup skipped: reason=below_min_lot profitCandidates=%d budgetBefore=%.2f preferOldGrid=%s",
                                profitCount, budgetBefore,
                                (InpPairCleanupPreferOldGridLosers ? "true" : "false")));
            LogTrendRescueCleanupDiag("pair", "below_min_lot",
                                      StringFormat("budgetBefore=%.2f profitCandidates=%d", budgetBefore, profitCount));
           }
         else
           {
            TrendRescueBelowMinLotGuardClear("pair");
            Log(2, StringFormat("Straddle: trend rescue pair cleanup skipped: reason=no_fundable_loser profitCandidates=%d budgetBefore=%.2f preferOldGrid=%s",
                                profitCount, budgetBefore,
                                (InpPairCleanupPreferOldGridLosers ? "true" : "false")));
            LogTrendRescueCleanupDiag("pair", "no_fundable_loser",
                                      StringFormat("budgetBefore=%.2f profitCandidates=%d", budgetBefore, profitCount));
           }
         break;
        }
      TrendRescueBelowMinLotGuardClear("pair");

      TrendRescueProfitCandidate selectedProfit;
      selectedProfit = profits[selectedProfitIndex];
      LogAlways(StringFormat("Straddle: trend rescue pair cleanup selected: profitTicket #%I64u loserTicket #%I64u loserKind=%s estimatedProfit %.2f estimatedLoss %.2f estimatedCloseCost %.2f budgetBefore %.2f estimatedBudget %.2f reservedPairLossBudget %.2f reason=%s",
                             selectedProfit.ticket, selectedLoserTicket,
                             (selectedLoserIsTrendEntry ? "trend-entry" : "old-grid"),
                             selectedProfit.profit, selectedEstimatedLoss,
                             selectedEstimatedCloseCost, budgetBefore,
                             selectedEstimatedBudget, selectedReservedLossBudget,
                             reason));

      double bookedProfit = selectedProfit.profit;
      double profitCloseCost = selectedProfit.estimatedCloseCost;
      bool profitClosed = false;
      bool loserClosed = false;
      if(!CloseTrendRescuePairProfitTicket(selectedProfit.ticket, bookedProfit, profitCloseCost))
        {
         LogOp(StringFormat("Straddle: trend rescue pair cleanup skipped: reason=profit_close_failed profitTicket #%I64u loserTicket #%I64u estimatedProfit %.2f estimatedLoss %.2f estimatedCloseCost %.2f budgetBefore %.2f",
                            selectedProfit.ticket, selectedLoserTicket,
                            selectedProfit.profit, selectedEstimatedLoss,
                            selectedEstimatedCloseCost, budgetBefore));
         break;
        }
      profitClosed = true;
      InvalidateTrendRescueSnapshot();
      InvalidateTrendRescueBelowMinLotGuards();

      double balanceAfterProfit = AccountInfoDouble(ACCOUNT_BALANCE);
      double bookedBudgetAfterProfit = TrendRescuePairCleanupBudget(reason);
      double actualFloorSafeLoserBudget = MathMin(bookedBudgetAfterProfit,
                                                  VoluntaryLossBudgetAtBalance(balanceAfterProfit,
                                                                               MoneyInput(InpPairCleanupBufferUSD)));
      selectedReservedLossBudget = actualFloorSafeLoserBudget;
      LogAlways(StringFormat("Straddle: trend rescue pair cleanup profit booked: profitTicket #%I64u originalLoserTicket #%I64u estimatedProfit %.2f estimatedCloseCost %.2f balance %.2f->%.2f budget %.2f->%.2f reservedPairLossBudget %.2f actualFloorSafeLoserBudget %.2f reason=%s",
                             selectedProfit.ticket, selectedLoserTicket,
                             bookedProfit, profitCloseCost,
                             balanceBefore, balanceAfterProfit,
                             budgetBefore, bookedBudgetAfterProfit,
                             selectedReservedLossBudget, actualFloorSafeLoserBudget,
                             reason));

      ulong originalLoserTicket = selectedLoserTicket;
      selectedLoserIsTrendEntry = false;
      selectedLoserTicket = 0;
      selectedCloseLots = 0.0;
      selectedEstimatedLoss = 0.0;
      selectedEstimatedCloseCost = 0.0;
      bool refreshedBelowMinLot = false;

      if(actualFloorSafeLoserBudget <= 0.0 ||
         !SelectTrendRescuePairLoser(actualFloorSafeLoserBudget, selectedLoserIsTrendEntry,
                                     selectedLoserTicket, selectedCloseLots,
                                     selectedEstimatedLoss, selectedEstimatedCloseCost,
                                     refreshedBelowMinLot))
        {
         string detail = (refreshedBelowMinLot ? "refreshed_below_min_lot" : "no_refreshed_fundable_loser");
         CarryTrendRescuePairOrphanedReserve(MathMax(actualFloorSafeLoserBudget, bookedBudgetAfterProfit), detail,
                                             selectedProfit.ticket, originalLoserTicket,
                                             balanceAfterProfit);
         LogOp(StringFormat("Straddle: trend rescue pair cleanup invariant: reason=pair_cleanup_invariant_profit_without_loser detail=%s profitTicket #%I64u originalLoserTicket #%I64u actualFloorSafeLoserBudget %.2f bookedBudgetAfterProfit %.2f balanceAfterProfit %.2f orphanedPairProfitReserve %.2f profitClosed=%s loserClosed=%s",
                            detail,
                            selectedProfit.ticket, originalLoserTicket,
                            actualFloorSafeLoserBudget, bookedBudgetAfterProfit,
                            balanceAfterProfit,
                            g_deferredPairProfitReserve,
                            (profitClosed ? "true" : "false"),
                            (loserClosed ? "true" : "false")));
         break;
        }

      if(PositionSelectByTicket(selectedLoserTicket))
        {
         double loserVolume = PositionGetDouble(POSITION_VOLUME);
         double loserProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         double refreshedEstimatedLoss = TrendRescueCleanupEstimatedLoss(selectedLoserTicket, loserProfitAndSwap, loserVolume, selectedCloseLots);
         if(refreshedEstimatedLoss > selectedReservedLossBudget + 1.0e-6)
           {
            CarryTrendRescuePairOrphanedReserve(MathMax(selectedReservedLossBudget, bookedBudgetAfterProfit), "refreshed_loss_exceeded",
                                                selectedProfit.ticket, originalLoserTicket,
                                                balanceAfterProfit);
            LogOp(StringFormat("Straddle: trend rescue pair cleanup invariant: reason=pair_cleanup_invariant_profit_without_loser detail=refreshed_loss_exceeded profitTicket #%I64u loserTicket #%I64u originalLoserTicket #%I64u refreshedLoss %.2f reservedPairLossBudget %.2f orphanedPairProfitReserve %.2f profitClosed=%s loserClosed=%s",
                               selectedProfit.ticket, selectedLoserTicket,
                               originalLoserTicket, refreshedEstimatedLoss,
                               selectedReservedLossBudget,
                               g_deferredPairProfitReserve,
                               (profitClosed ? "true" : "false"),
                               (loserClosed ? "true" : "false")));
            break;
            }
          selectedEstimatedLoss = refreshedEstimatedLoss;
          selectedEstimatedCloseCost = EstimatedCloseCost(selectedCloseLots, selectedLoserTicket);
         }
      else
        {
         CarryTrendRescuePairOrphanedReserve(MathMax(actualFloorSafeLoserBudget, bookedBudgetAfterProfit), "refreshed_loser_missing",
                                             selectedProfit.ticket, originalLoserTicket,
                                             balanceAfterProfit);
         LogOp(StringFormat("Straddle: trend rescue pair cleanup invariant: reason=pair_cleanup_invariant_profit_without_loser detail=refreshed_loser_missing profitTicket #%I64u loserTicket #%I64u originalLoserTicket #%I64u actualFloorSafeLoserBudget %.2f orphanedPairProfitReserve %.2f profitClosed=%s loserClosed=%s",
                            selectedProfit.ticket, selectedLoserTicket,
                            originalLoserTicket, actualFloorSafeLoserBudget,
                            g_deferredPairProfitReserve,
                            (profitClosed ? "true" : "false"),
                            (loserClosed ? "true" : "false")));
         break;
        }

      double budgetBeforeLoser = selectedReservedLossBudget;
      if(CloseTrendRescuePairLoser(selectedLoserTicket, selectedLoserIsTrendEntry,
                                   selectedCloseLots, selectedEstimatedLoss,
                                   selectedReservedLossBudget))
        {
         actions++;
         loserClosed = true;
         closedAny = true;
         double budgetAfterLoser = TrendRescuePairCleanupBudget(reason);
         LogAlways(StringFormat("Straddle: trend rescue pair cleanup loser close: profitTicket #%I64u loserTicket #%I64u loserKind=%s closeLots %.2f estimatedProfit %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f->%.2f->%.2f result=closed reason=%s",
                                selectedProfit.ticket, selectedLoserTicket,
                                (selectedLoserIsTrendEntry ? "trend-entry" : "old-grid"),
                                selectedCloseLots, bookedProfit, selectedEstimatedLoss,
                                selectedEstimatedCloseCost, budgetBefore, budgetBeforeLoser,
                                budgetAfterLoser, reason));
          continue;
         }

      CarryTrendRescuePairOrphanedReserve(MathMax(selectedReservedLossBudget, bookedBudgetAfterProfit), "loser_close_failed",
                                          selectedProfit.ticket, originalLoserTicket,
                                          balanceAfterProfit);
      LogOp(StringFormat("Straddle: trend rescue pair cleanup invariant: reason=pair_cleanup_invariant_profit_without_loser detail=loser_close_failed profitTicket #%I64u loserTicket #%I64u loserKind=%s closeLots %.2f estimatedProfit %.2f estimatedLoss %.2f estimatedCloseCost %.2f budgetBefore %.2f budgetAfterProfit %.2f orphanedPairProfitReserve %.2f profitClosed=%s loserClosed=%s action result=loser_close_failed",
                          selectedProfit.ticket, selectedLoserTicket,
                          (selectedLoserIsTrendEntry ? "trend-entry" : "old-grid"),
                          selectedCloseLots, bookedProfit, selectedEstimatedLoss,
                          selectedEstimatedCloseCost, budgetBefore,
                          selectedReservedLossBudget,
                          g_deferredPairProfitReserve,
                          (profitClosed ? "true" : "false"),
                          (loserClosed ? "true" : "false")));
      break;
     }

   if(actions >= InpPairCleanupMaxActionsPerTick)
      Log(2, StringFormat("Straddle: trend rescue pair cleanup skipped: reason=max_actions actions=%d maxActions=%d",
                          actions, InpPairCleanupMaxActionsPerTick));

   return closedAny;
  }

void AppendFloatingPairLoserCandidate(FloatingPairLoserCandidate &dest[],
                                      const CleanupCandidate &source,
                                      const bool isTrendEntry)
  {
   int n = ArraySize(dest);
   ArrayResize(dest, n + 1);
   dest[n].ticket = source.ticket;
   dest[n].volume = source.volume;
   dest[n].loss = source.loss;
   dest[n].lossPerLot = source.lossPerLot;
   dest[n].swap = source.swap;
   dest[n].openTime = source.openTime;
   dest[n].type = source.type;
   dest[n].isTrendEntry = isTrendEntry;
  }

int CollectFloatingPairProfitCandidates(TrendRescueProfitCandidate &candidates[],
                                        const ENUM_POSITION_TYPE favorableType)
  {
   ArrayResize(candidates, 0);
   TrendRescueProfitCandidate allProfits[];
   int allCount = CollectTrendRescuePairProfitCandidates(allProfits);
   int n = 0;
   for(int i = 0; i < allCount; i++)
     {
      if(allProfits[i].type != favorableType)
         continue;
      double netProfit = allProfits[i].profit - allProfits[i].estimatedCloseCost;
      if(netProfit <= 0.0)
         continue;
      ArrayResize(candidates, n + 1);
      candidates[n] = allProfits[i];
      n++;
     }
   return n;
  }

int CollectFloatingPairLoserCandidates(FloatingPairLoserCandidate &candidates[],
                                       const ENUM_POSITION_TYPE loserType)
  {
   ArrayResize(candidates, 0);

   CleanupCandidate oldLosers[];
   int oldCount = CollectStaleOldGridCleanupCandidates(oldLosers);
   for(int i = 0; i < oldCount; i++)
     {
      if(oldLosers[i].type == loserType)
         AppendFloatingPairLoserCandidate(candidates, oldLosers[i], false);
     }

   CleanupCandidate redTrendEntries[];
   int redCount = CollectStaleTrendEntryCleanupCandidates(redTrendEntries);
   for(int i = 0; i < redCount; i++)
     {
      if(redTrendEntries[i].type == loserType)
         AppendFloatingPairLoserCandidate(candidates, redTrendEntries[i], true);
     }

   return ArraySize(candidates);
  }

bool FloatingPairLoserBetter(const FloatingPairLoserCandidate &a,
                             const FloatingPairLoserCandidate &b)
  {
   if(MathAbs(a.lossPerLot - b.lossPerLot) > 1.0e-8)
      return (a.lossPerLot > b.lossPerLot);
   if(MathAbs(a.loss - b.loss) > 1.0e-8)
      return (a.loss > b.loss);
   if(a.openTime != b.openTime)
      return (a.openTime < b.openTime);
   return (a.ticket < b.ticket);
  }

void SortFloatingPairLoserCandidates(FloatingPairLoserCandidate &candidates[])
  {
   int n = ArraySize(candidates);
   for(int i = 0; i < n - 1; i++)
     {
      int best = i;
      for(int j = i + 1; j < n; j++)
        {
         if(FloatingPairLoserBetter(candidates[j], candidates[best]))
            best = j;
        }
      if(best != i)
        {
         FloatingPairLoserCandidate tmp = candidates[i];
         candidates[i] = candidates[best];
         candidates[best] = tmp;
        }
     }
  }

bool SelectFloatingPairLoser(const double budget,
                             const ENUM_POSITION_TYPE loserType,
                             bool &isTrendEntry,
                             ulong &ticket,
                             double &closeLots,
                             double &estimatedLoss,
                             double &estimatedCloseCost,
                             bool &belowMinLot,
                             int &candidateCount)
  {
   ticket = 0;
   closeLots = 0.0;
   estimatedLoss = 0.0;
   estimatedCloseCost = 0.0;
   isTrendEntry = false;
   belowMinLot = false;

   FloatingPairLoserCandidate losers[];
   candidateCount = CollectFloatingPairLoserCandidates(losers, loserType);
   if(candidateCount <= 0)
      return false;
   SortFloatingPairLoserCandidates(losers);

   for(int i = 0; i < candidateCount; i++)
     {
      double candidateLots = 0.0;
      double candidateLoss = 0.0;
      double candidateCloseCost = 0.0;
      bool candidateBelowMinLot = false;
      if(PrepareTrendRescuePairLoserTicket(losers[i].ticket, losers[i].isTrendEntry,
                                           budget, candidateLots, candidateLoss,
                                           candidateCloseCost, candidateBelowMinLot))
        {
         isTrendEntry = losers[i].isTrendEntry;
         ticket = losers[i].ticket;
         closeLots = candidateLots;
         estimatedLoss = candidateLoss;
         estimatedCloseCost = candidateCloseCost;
         return true;
        }
      if(candidateBelowMinLot)
         belowMinLot = true;
     }

   return false;
  }

double FloatingPairCleanupUsableBudget(const string reason,
                                       const double reservedProfitBudget,
                                       double &bookedBudget,
                                       double &floorBudget)
  {
   bookedBudget = TrendRescuePairCleanupBudget(reason);
   floorBudget = VoluntaryLossBudget(MoneyInput(InpPairCleanupBufferUSD));
   return MathMax(0.0, NormalizeAccountMoney(MathMin(reservedProfitBudget,
                                                     MathMin(bookedBudget, floorBudget))));
  }

bool FloatingPairCleanupSafetyOk(const string reason)
  {
   double margin = AccountInfoDouble(ACCOUNT_MARGIN);
   double marginLevel = AccountInfoDouble(ACCOUNT_MARGIN_LEVEL);
   if(InpFloatingPairCleanupMinMarginLevelPct > 0.0 && margin > 0.0)
     {
      if(marginLevel <= 0.0 ||
         marginLevel < InpFloatingPairCleanupMinMarginLevelPct)
        {
         LogTrendRescueCleanupDiag("floating-pair", "margin_level",
                                   StringFormat("reason=%s marginLevel=%.2f minMarginLevel=%.2f",
                                                reason, marginLevel,
                                                InpFloatingPairCleanupMinMarginLevelPct));
         return false;
        }
     }

   double floorBalance = VoluntaryLossFloorBalance(MoneyInput(InpPairCleanupBufferUSD));
   double minEquityBuffer = MoneyInput(InpFloatingPairCleanupMinEquityBufferUSD);
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   if(minEquityBuffer > 0.0 && equity < floorBalance + minEquityBuffer)
     {
      LogTrendRescueCleanupDiag("floating-pair", "equity_buffer",
                                StringFormat("reason=%s equity=%.2f floorBalance=%.2f minEquityBuffer=%.2f",
                                             reason, equity, floorBalance, minEquityBuffer));
      return false;
     }

   return true;
  }

bool TryTrendRescueFloatingPairCleanup(const string reason)
  {
   if(!RealizedLossClosesAllowed())
      return false;
   if(!g_trendRescueActive || !InpUseTrendRescueFloatingPairCleanup)
      return false;
   if(InpFloatingPairCleanupMaxProfitTicketsPerTick <= 0 ||
      InpFloatingPairCleanupMaxLoserActionsPerTick <= 0 ||
      InpFloatingPairCleanupProfitShare <= 0.0)
     {
      LogTrendRescueCleanupDiag("floating-pair", "max_actions",
                                StringFormat("profitShare=%.2f maxProfitTickets=%d maxLoserActions=%d",
                                             InpFloatingPairCleanupProfitShare,
                                             InpFloatingPairCleanupMaxProfitTicketsPerTick,
                                             InpFloatingPairCleanupMaxLoserActionsPerTick));
      return false;
     }
   if(!FloatingPairCleanupSafetyOk(reason))
      return false;

   int oldCount = 0, redCount = 0, staleCount = 0;
   double largestStaleLoss = 0.0, totalStaleLoss = 0.0, equityDD = 0.0;
   string staleReason = "";
   if(!StaleTradeThresholdsBreached(staleCount, oldCount, redCount,
                                    largestStaleLoss, totalStaleLoss,
                                    equityDD, staleReason))
     {
      LogTrendRescueCleanupDiag("floating-pair", "stale_pressure_absent",
                                StringFormat("reason=%s staleReason=%s candidates=%d oldCount=%d redCount=%d largestStaleLoss=%.2f totalStaleLoss=%.2f equityDD=%.2f",
                                             reason, staleReason, staleCount,
                                             oldCount, redCount, largestStaleLoss,
                                             totalStaleLoss, equityDD));
      return false;
     }

   ENUM_POSITION_TYPE favorableType = (IsTrendRescueBuy() ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);
   ENUM_POSITION_TYPE loserType = (favorableType == POSITION_TYPE_BUY ? POSITION_TYPE_SELL : POSITION_TYPE_BUY);

   TrendRescueProfitCandidate profits[];
   int profitCount = CollectFloatingPairProfitCandidates(profits, favorableType);
   if(profitCount <= 0)
     {
      LogTrendRescueCleanupDiag("floating-pair", "no_profit_candidates",
                                StringFormat("reason=%s favorableType=%s",
                                             reason,
                                             (favorableType == POSITION_TYPE_BUY ? "BUY" : "SELL")));
      return false;
     }
   SortTrendRescuePairProfitCandidates(profits);

   double totalFloatingNetProfit = 0.0;
   for(int i = 0; i < profitCount; i++)
      totalFloatingNetProfit = NormalizeAccountMoney(totalFloatingNetProfit +
                                                     MathMax(0.0, profits[i].profit - profits[i].estimatedCloseCost));
   double share = MathMin(1.0, MathMax(0.0, InpFloatingPairCleanupProfitShare));
   double profitShareBudget = NormalizeAccountMoney(totalFloatingNetProfit * share);
   double existingReserveBudget = TrendRescuePairOrphanedReserveBudget(reason + "-existing");

   bool closedAny = false;
   int profitClosed = 0;
   int loserActions = 0;
   double usableProfitBudget = existingReserveBudget;
   double newlyBookedReserveBudget = 0.0;
   double profitShareUsed = 0.0;
   ulong lastProfitTicket = 0;
   ulong lastLoserTicket = 0;
   string reserveCarryDetail = "";

   while(profitClosed < InpFloatingPairCleanupMaxProfitTicketsPerTick &&
         loserActions < InpFloatingPairCleanupMaxLoserActionsPerTick)
     {
      if(!FloatingPairCleanupSafetyOk(reason + "-profit"))
         break;

      double shareRemaining = MathMax(0.0, NormalizeAccountMoney(profitShareBudget - profitShareUsed));
      if(shareRemaining <= 0.0)
        {
         LogTrendRescueCleanupDiag("floating-pair", "profit_share_exhausted",
                                   StringFormat("reason=%s profitShareBudget=%.2f profitShareUsed=%.2f usableProfitBudget=%.2f existingReserveBudget=%.2f",
                                                reason, profitShareBudget,
                                                profitShareUsed, usableProfitBudget,
                                                existingReserveBudget));
         break;
        }

      TrendRescueProfitCandidate freshProfits[];
      int freshProfitCount = CollectFloatingPairProfitCandidates(freshProfits, favorableType);
      if(freshProfitCount <= 0)
        {
         LogTrendRescueCleanupDiag("floating-pair", "no_profit_candidates",
                                   StringFormat("reason=%s profitClosed=%d loserActions=%d usableProfitBudget=%.2f",
                                                reason, profitClosed,
                                                loserActions, usableProfitBudget));
         break;
        }
      SortTrendRescuePairProfitCandidates(freshProfits);

      int selectedProfitIndex = -1;
      for(int i = 0; i < freshProfitCount; i++)
        {
         double netProfit = MathMax(0.0, freshProfits[i].profit - freshProfits[i].estimatedCloseCost);
         if(netProfit <= 0.0)
            continue;
         selectedProfitIndex = i;
         break;
        }
      if(selectedProfitIndex < 0)
        {
         LogTrendRescueCleanupDiag("floating-pair", "no_net_profit_candidate",
                                   StringFormat("reason=%s freshProfitCount=%d profitClosed=%d loserActions=%d",
                                                reason, freshProfitCount,
                                                profitClosed, loserActions));
         break;
        }

      TrendRescueProfitCandidate selectedProfit;
      selectedProfit = freshProfits[selectedProfitIndex];
      double beforeBalance = AccountInfoDouble(ACCOUNT_BALANCE);
      double bookedProfit = selectedProfit.profit;
      double profitCloseCost = selectedProfit.estimatedCloseCost;
      if(!CloseTrendRescuePairProfitTicket(selectedProfit.ticket, bookedProfit, profitCloseCost))
        {
         LogTrendRescueCleanupDiag("floating-pair", "profit_close_failed",
                                   StringFormat("reason=%s profitTicket #%I64u estimatedProfit=%.2f profitClosed=%d loserActions=%d",
                                                reason, selectedProfit.ticket,
                                                selectedProfit.profit,
                                                profitClosed, loserActions));
         break;
        }

      profitClosed++;
      lastProfitTicket = selectedProfit.ticket;
      double balanceAfterProfit = AccountInfoDouble(ACCOUNT_BALANCE);
      double bookedGain = MathMax(0.0, NormalizeAccountMoney(balanceAfterProfit - beforeBalance));
      double creditedBudget = MathMin(bookedGain, shareRemaining);
      profitShareUsed = NormalizeAccountMoney(profitShareUsed + creditedBudget);
      usableProfitBudget = NormalizeAccountMoney(usableProfitBudget + creditedBudget);
      newlyBookedReserveBudget = NormalizeAccountMoney(newlyBookedReserveBudget + creditedBudget);
      InvalidateTrendRescueSnapshot();
      InvalidateTrendRescueBelowMinLotGuards();

      LogAlways(StringFormat("Straddle: floating pair cleanup profit booked: profitTicket #%I64u type %s bookedGain %.2f creditedBudget %.2f usableProfitBudget %.2f shareBudget %.2f reason=%s",
                             selectedProfit.ticket,
                             (selectedProfit.type == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                             bookedGain, creditedBudget, usableProfitBudget,
                             profitShareBudget, reason));

      if(!FloatingPairCleanupSafetyOk(reason + "-loser"))
        {
         reserveCarryDetail = "floating_pair_safety_after_profit";
         break;
        }

      double bookedBudget = 0.0, floorBudget = 0.0;
      double currentBudget = FloatingPairCleanupUsableBudget(reason, usableProfitBudget,
                                                            bookedBudget, floorBudget);
      if(currentBudget <= 0.0)
        {
         LogTrendRescueCleanupDiag("floating-pair", "no_budget",
                                   StringFormat("reason=%s usableProfitBudget=%.2f bookedBudget=%.2f floorBudget=%.2f",
                                                reason, usableProfitBudget,
                                                bookedBudget, floorBudget));
         reserveCarryDetail = "floating_pair_no_budget_after_profit";
         break;
        }

      bool loserIsTrendEntry = false;
      ulong loserTicket = 0;
      double closeLots = 0.0, estimatedLoss = 0.0, estimatedCloseCost = 0.0;
      bool belowMinLot = false;
      int loserCandidates = 0;
      if(!SelectFloatingPairLoser(currentBudget, loserType, loserIsTrendEntry,
                                  loserTicket, closeLots, estimatedLoss,
                                  estimatedCloseCost, belowMinLot, loserCandidates))
        {
         LogTrendRescueCleanupDiag("floating-pair",
                                   (belowMinLot ? "needs_more_budget" : "no_fundable_loser"),
                                   StringFormat("reason=%s usableProfitBudget=%.2f currentBudget=%.2f bookedBudget=%.2f floorBudget=%.2f loserCandidates=%d",
                                                reason, usableProfitBudget, currentBudget,
                                                bookedBudget, floorBudget, loserCandidates));
         reserveCarryDetail = (belowMinLot ? "floating_pair_below_min_lot" : "floating_pair_no_fundable_loser");
         break;
        }

      lastLoserTicket = loserTicket;
      if(!PositionSelectByTicket(loserTicket))
        {
         LogTrendRescueCleanupDiag("floating-pair", "refreshed_loser_missing",
                                   StringFormat("reason=%s loserTicket #%I64u currentBudget=%.2f usableProfitBudget=%.2f",
                                                reason, loserTicket,
                                                currentBudget, usableProfitBudget));
         reserveCarryDetail = "floating_pair_refreshed_loser_missing";
         break;
        }

      double loserVolume = PositionGetDouble(POSITION_VOLUME);
      double loserProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      double refreshedEstimatedLoss = TrendRescueCleanupEstimatedLoss(loserTicket, loserProfitAndSwap,
                                                                      loserVolume, closeLots);
      if(refreshedEstimatedLoss > currentBudget + 1.0e-6)
        {
         LogTrendRescueCleanupDiag("floating-pair", "refreshed_loss_exceeded",
                                   StringFormat("reason=%s loserTicket #%I64u refreshedLoss=%.2f currentBudget=%.2f usableProfitBudget=%.2f",
                                                reason, loserTicket,
                                                refreshedEstimatedLoss,
                                                currentBudget, usableProfitBudget));
         reserveCarryDetail = "floating_pair_refreshed_loss_exceeded";
         break;
        }
      estimatedLoss = refreshedEstimatedLoss;
      estimatedCloseCost = EstimatedCloseCost(closeLots, loserTicket);

      double beforeBudget = currentBudget;
      if(CloseTrendRescuePairLoser(loserTicket, loserIsTrendEntry,
                                   closeLots, estimatedLoss, currentBudget))
        {
         loserActions++;
         closedAny = true;
         if(existingReserveBudget > 0.0)
           {
            ResetOrphanedPairProfitReserve();
            existingReserveBudget = 0.0;
           }
         usableProfitBudget = MathMax(0.0, NormalizeAccountMoney(usableProfitBudget - estimatedLoss));
         InvalidateTrendRescueSnapshot();
         InvalidateTrendRescueBelowMinLotGuards();
         LogAlways(StringFormat("Straddle: floating pair cleanup loser close: profitTicketsClosed=%d loserTicket #%I64u loserKind=%s type %s closeLots %.2f estimatedLoss %.2f estimatedCloseCost %.2f budget %.2f remainingProfitBudget %.2f reason=%s",
                                profitClosed, loserTicket,
                                (loserIsTrendEntry ? "trend-entry" : "old-grid"),
                                (loserType == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                                closeLots, estimatedLoss, estimatedCloseCost,
                                beforeBudget, usableProfitBudget, reason));
         continue;
        }

      LogTrendRescueCleanupDiag("floating-pair", "loser_close_failed",
                                StringFormat("reason=%s loserTicket #%I64u closeLots %.2f estimatedLoss %.2f budget=%.2f usableProfitBudget=%.2f",
                                             reason, loserTicket, closeLots,
                                             estimatedLoss, currentBudget,
                                             usableProfitBudget));
      reserveCarryDetail = "floating_pair_loser_close_failed";
      break;
     }

   double carryBudget = 0.0;
   if(closedAny)
      carryBudget = usableProfitBudget;
   else if(profitClosed > 0)
      carryBudget = newlyBookedReserveBudget;

   if(carryBudget > 0.0)
     {
      string detail = reserveCarryDetail;
      if(detail == "")
         detail = (closedAny ? "floating_pair_remaining_reserve" : "floating_pair_profit_without_loser");
      CarryTrendRescuePairOrphanedReserve(carryBudget, detail,
                                          lastProfitTicket, lastLoserTicket,
                                          AccountInfoDouble(ACCOUNT_BALANCE));
     }

   if(profitClosed >= InpFloatingPairCleanupMaxProfitTicketsPerTick)
      LogTrendRescueCleanupDiag("floating-pair", "max_profit_tickets",
                                StringFormat("reason=%s profitClosed=%d maxProfitTickets=%d",
                                             reason, profitClosed,
                                             InpFloatingPairCleanupMaxProfitTicketsPerTick));
   if(loserActions >= InpFloatingPairCleanupMaxLoserActionsPerTick)
      LogTrendRescueCleanupDiag("floating-pair", "max_loser_actions",
                                StringFormat("reason=%s loserActions=%d maxLoserActions=%d",
                                             reason, loserActions,
                                             InpFloatingPairCleanupMaxLoserActionsPerTick));

   return closedAny;
  }

bool RescueHedgeLossTriggered()
  {
   double openLots = 0.0;
   double floating = OpenFloatingPL(openLots);
   double cycleNet = CycleRealized() + floating;
   double trigger = MathMax(0.0, MoneyInput(InpRescueHedgeTriggerLossUSD));
   if(trigger <= 0.0)
      return true;
   return (floating <= -trigger || cycleNet <= -trigger);
  }

bool RescueHedgeMarginOk(const bool hedgeBuy,
                          const double lots,
                          const double price)
  {
   double accountMargin = AccountInfoDouble(ACCOUNT_MARGIN);
   double marginLevel = AccountInfoDouble(ACCOUNT_MARGIN_LEVEL);
   if(InpRescueHedgeMinMarginLevelPct > 0.0 && accountMargin > 0.0)
     {
      if(marginLevel <= 0.0 || marginLevel < InpRescueHedgeMinMarginLevelPct)
        {
         Log(2, StringFormat("Straddle: rescue hedge skipped - margin level %.2f%% below %.2f%%",
                             marginLevel, InpRescueHedgeMinMarginLevelPct));
         return false;
        }
     }

   double requiredMargin = 0.0;
   ENUM_ORDER_TYPE orderType = (hedgeBuy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL);
   if(!OrderCalcMargin(orderType, g_sym, lots, price, requiredMargin))
     {
      LogOp(StringFormat("Straddle: rescue hedge skipped - OrderCalcMargin failed for %s %.2f",
                         (hedgeBuy ? "BUY" : "SELL"), lots));
      return false;
     }

   double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
   double closeBuffer = MoneyInput(InpCleanupCostBufferUSD);
   if(requiredMargin <= 0.0 || freeMargin <= requiredMargin + closeBuffer)
     {
      Log(2, StringFormat("Straddle: rescue hedge skipped - freeMargin %.2f required %.2f buffer %.2f",
                          freeMargin, requiredMargin, closeBuffer));
      return false;
     }

   return true;
  }

bool TryOpenRescueHedge(const double bid, const double ask)
  {
   if(!RescueHedgeStateActive() || !InpUseRescueHedge) // 0.0.34: reachable in trend rescue too
      return false;
   if(!IsHedgingAccount())
     {
      if(!g_rescueHedgeModeLogged)
        {
         g_rescueHedgeModeLogged = true;
         Log(1, "Straddle: rescue hedge skipped - account margin mode is not RETAIL_HEDGING");
        }
      return false;
     }
   if(InpRescueMaxHedges <= 0 || CountRescueHedges() >= InpRescueMaxHedges)
      return false;
   if(AccountInfoInteger(ACCOUNT_HEDGE_ALLOWED) == 0)
     {
      LogOp("Straddle: rescue hedge skipped - ACCOUNT_HEDGE_ALLOWED is false");
      return false;
     }
   if(InpRescueHedgeCooldownSec > 0 && g_lastRescueHedgeTime > 0 &&
      (TimeCurrent() - g_lastRescueHedgeTime) < InpRescueHedgeCooldownSec)
      return false;
   if(!RescueHedgeLossTriggered())
      return false;
   if(bid <= 0.0 || ask <= 0.0)
      return false;

   // 0.0.37 (C3): net-size via the shared OpenNetHedge path. coverFrac uses the
   // tunable InpRescueHedgeCoverageFrac (default 0.0 reproduces the 0.01-token
   // behavior bit-for-bit). All per-call gates above are unchanged.
   return OpenNetHedge(bid, ask, InpRescueHedgeCoverageFrac, "rescue");
  }

//+------------------------------------------------------------------+
//| 0.0.37 (C3/C5): single hedge-open authority. Sizes a net-offset    |
//| STR RHG hedge to MAX(InpRescueHedgeLot, |net| * coverFrac) clamped  |
//| DOWN to |net| via NormalizeCloseLotsDown (never rounds past net),   |
//| then runs the SAME OrderSend/guard/retry path used by both the      |
//| rescue hedge (coverFrac=InpRescueHedgeCoverageFrac) and the equity  |
//| backstop (coverFrac=1.0). On a successful send it stamps + persists  |
//| g_lastRescueHedgeTime so the rescue-hedge open and the backstop open |
//| are mutually exclusive within a tick. Callers own their own outer    |
//| gates (state/account-hedging/cooldown/max-hedges/loss-trigger).      |
//+------------------------------------------------------------------+
bool OpenNetHedge(const double bid, const double ask, const double coverFrac, const string reason)
  {
   if(bid <= 0.0 || ask <= 0.0)
      return false;

   double buyLots = 0.0, sellLots = 0.0;
   NetExposureLots(buyLots, sellLots);
   double netLots = buyLots - sellLots;
   if(MathAbs(netLots) <= 1.0e-8)
      return false;

   bool hedgeBuy = (netLots < 0.0);

   // 1.1.1: while trend one-side bias is active, optionally block hedges that
   // open AGAINST the trend (covers rescue hedge + equity-backstop paths).
   if(InpUseTrendOneSideGrid && InpTrendOneSideBlockHedge)
     {
      const int hedgeBias = TrendOneSideBias();
      if((hedgeBias > 0 && !hedgeBuy) || (hedgeBias < 0 && hedgeBuy))
        {
         const datetime nowBlock = TimeCurrent();
         const bool biasChanged = (hedgeBias != g_lastTrendOneSideHedgeBlockBias);
         const bool timeOk = (g_lastTrendOneSideHedgeBlockLog == 0 ||
                              (nowBlock - g_lastTrendOneSideHedgeBlockLog) >= 60);
         if(biasChanged || timeOk)
           {
            Log(1, StringFormat("Straddle: %s hedge blocked by trend one-side bias=%d (would open %s against trend)",
                                reason, hedgeBias, (hedgeBuy ? "BUY" : "SELL")));
            g_lastTrendOneSideHedgeBlockLog = nowBlock;
            g_lastTrendOneSideHedgeBlockBias = hedgeBias;
           }
         else
            Log(2, StringFormat("Straddle: %s hedge blocked by trend one-side bias=%d (would open %s against trend)",
                                reason, hedgeBias, (hedgeBuy ? "BUY" : "SELL")));
         return false;
        }
     }

   double maxHedgeLots = MathAbs(netLots);
   double frac = MathMax(0.0, MathMin(1.0, coverFrac));
   double desiredHedge = MathMax(InpRescueHedgeLot, maxHedgeLots * frac);
   double hedgeLots = NormalizeCloseLotsDown(desiredHedge, maxHedgeLots);
   if(hedgeLots <= 0.0)
     {
      Log(2, StringFormat("Straddle: %s hedge skipped - requested lot %.2f cannot fit net exposure %.2f without rounding up",
                          reason, desiredHedge, maxHedgeLots));
      return false;
     }

   double price = (hedgeBuy ? ask : bid);
   if(!RescueHedgeMarginOk(hedgeBuy, hedgeLots, price))
      return false;

   LogRescueStatus("before hedge open", true);
   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      double freshBid = 0.0, freshAsk = 0.0;
      if(!RefreshCurrentPrices(freshBid, freshAsk))
         return false;
      price = (hedgeBuy ? freshAsk : freshBid);
      if(!RescueHedgeMarginOk(hedgeBuy, hedgeLots, price))
         return false;

      StrActionOutcome outcome=ExecuteMarketDeal((hedgeBuy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL),
                                                 hedgeLots,0.0,0.0,"STR RHG",
                                                 InpRescueHedgeMinMarginLevelPct,
                                                 MoneyInput(InpCleanupCostBufferUSD));
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         // 0.0.40 O1: hedge position opened -> book mutated. Set BEFORE the
         // in-branch LogRescueStatus (which reads OpenFloatingPL / counts) so
         // the success log reflects the post-open book.
         g_bookDirty = true;
         g_lastRescueHedgeTime = TimeCurrent();
         PersistRescueHedgeTime();
         LogAlways(StringFormat("Straddle: %s hedge opened %s %.2f against net exposure %.2f (coverFrac %.2f) at %s",
                                reason, (hedgeBuy ? "BUY" : "SELL"), hedgeLots, maxHedgeLots, frac,
                                DoubleToString(price, g_digits)));
         Log(1, StringFormat("Straddle: rescue recenter hedge opened at current price %s; fixed grid anchor unchanged until rescue is flat, next normal cycle anchors fresh",
                             DoubleToString(price, g_digits)));
         LogRescueStatus("after hedge open", true);
         return true;
        }

      switch(ClassifyRetcode(rc))
        {
         case ACT_SUCCESS:
            return false;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:
         case ACT_ABORT_CYCLE:
            return false;
         default:
            LogOp(StringFormat("Straddle: %s hedge %s %.2f failed, retcode %u (%s)",
                               reason,(hedgeBuy ? "BUY" : "SELL"),hedgeLots,rc,outcome.result_comment));
            return false;
         }
      }

   return false;
  }

//+------------------------------------------------------------------+
//| 0.0.37 (C4): sticky-hold gate for a profitable rescue hedge. HOLDS  |
//| (skips harvest) while equity DD is still severe (>= InpRescueHedge  |
//| HoldDDUSD) AND the hold has NOT exceeded its max age (InpRescueHedge |
//| MaxHoldHours). Keeps the protective offset on through the crash      |
//| instead of harvesting it at +InpRescueHedgeHarvestUSD and re-        |
//| exposing the pile mid-move. Defaults (HoldDD=0) -> never holds, so   |
//| legacy harvest behavior is reproduced bit-for-bit. Only DEFERS a     |
//| close; it never opens exposure or realizes a loss.                  |
//+------------------------------------------------------------------+
bool RescueHedgeStickyHold()
  {
   if(InpRescueHedgeHoldDDUSD <= 0.0)
      return false;
   if(EquityFloatingDrawdown() < MoneyInput(InpRescueHedgeHoldDDUSD))
      return false;
   // bound the hold so a delta-neutral hedge cannot become a permanent swap
   // drain: once the hold age exceeds the max-hold window, allow harvest again.
   bool maxHoldExceeded = (g_rescueHedgeMaxHoldSeconds > 0 && g_lastRescueHedgeTime > 0 &&
                           TimeCurrent() >= g_lastRescueHedgeTime &&
                           (long)(TimeCurrent() - g_lastRescueHedgeTime) >= g_rescueHedgeMaxHoldSeconds);
   if(maxHoldExceeded)
      return false;
   return true;
  }

bool CloseRescueHedgeIfProfitable(const ulong ticket)
  {
   if(!PositionSelectByTicket(ticket))
      return false;
   if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
      return false;
   if(PositionGetString(POSITION_SYMBOL) != g_sym)
      return false;
   if(!IsRescueHedgeComment(PositionGetString(POSITION_COMMENT)))
      return false;

   double profitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   double harvestTarget = MoneyInput(InpRescueHedgeHarvestUSD);
   if(profitAndSwap < harvestTarget)
      return false;
   // 0.0.37 (C4): protective hedge held while DD is still severe and within the
   // max-hold window - do not harvest on its own +P/L while it is still needed.
   if(RescueHedgeStickyHold())
      return false;

   LogRescueStatus("before hedge harvest", true);
   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      if(!PositionSelectByTicket(ticket))
         return true;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic ||
         PositionGetString(POSITION_SYMBOL) != g_sym ||
         !IsRescueHedgeComment(PositionGetString(POSITION_COMMENT)))
         return false;

      profitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(profitAndSwap < harvestTarget)
         return false;
      // 0.0.37 (C4): re-check the sticky hold inside the retry loop so a hedge is
      // not harvested on the same severe-DD tick it would otherwise be held.
      if(RescueHedgeStickyHold())
         return false;

      double closeVolume=PositionGetDouble(POSITION_VOLUME);
      StrActionOutcome outcome=ExecutePositionReduction(ticket,closeVolume,"STR RHG CLOSE");
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         // 0.0.40 O1: hedge closed (full/partial) -> book mutated. Set BEFORE
         // the in-branch LogRescueStatus (reads counts/floating).
         g_bookDirty = true;
         if(!PositionSelectByTicket(ticket))
           {
            Log(1, StringFormat("Straddle: rescue hedge harvested ticket #%I64u profitAndSwap %.2f >= %.2f",
                                ticket, profitAndSwap, harvestTarget));
            LogRescueStatus("after hedge harvest", true);
            return true;
           }
         continue;
        }

      switch(ClassifyRetcode(rc))
        {
         case ACT_SUCCESS:
            return false;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:
         case ACT_ABORT_CYCLE:
            return false;
         default:
            LogOp(StringFormat("Straddle: rescue hedge harvest close of position #%I64u failed, retcode %u (%s)",
                               ticket,rc,outcome.result_comment));
            return false;
        }
     }

   return !PositionSelectByTicket(ticket);
  }

bool TryHarvestRescueHedges()
  {
   // 0.0.34: reachable in trend rescue too.
   // 0.0.35: ALSO reachable whenever an STR RHG hedge is open, regardless of
   // state. Without this, a hedge that fully neutralizes net exposure flips
   // StuckExposureHedgeEligible()/RescueHedgeStateActive() to false in a normal
   // cycle and would be stranded un-harvestable. CountRescueHedges() is
   // magic+symbol-scoped to this EA's STR RHG comment only.
   if(!RescueHedgeStateActive() && CountRescueHedges() <= 0)
      return false;

   bool closedAny = false;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(!IsRescueHedgeComment(PositionGetString(POSITION_COMMENT)))
         continue;
      if(CloseRescueHedgeIfProfitable(ticket))
         closedAny = true;
     }
   return closedAny;
  }

//+------------------------------------------------------------------+
//| 0.0.37 (C5): ACCOUNT-LEVEL EQUITY-DD CIRCUIT BREAKER.               |
//| There is no equity ceiling anywhere else, so maxDD is an unbounded  |
//| tail. This bounds it WITHOUT realizing any loss: when floating      |
//| equity drawdown reaches InpEquityHardFlattenDDUSD the breaker ARMS   |
//| and opens a FULL-net STR RHG hedge (OpenNetHedge, coverFrac=1.0),    |
//| taking the book ~delta-neutral so equity DD plateaus near the       |
//| threshold instead of riding deeper. Two-threshold hysteresis        |
//| (release < arm) prevents arm/release flapping. It NEVER closes-all,  |
//| NEVER refuses to trade, and NEVER suppresses entries - the grid      |
//| keeps banking around the frozen book. It reuses the SAME            |
//| g_lastRescueHedgeTime cooldown stamp as the rescue hedge (floored to |
//| [1, 30]s here) so the rescue-hedge open and this open are mutually   |
//| exclusive within a tick. 0.0.41 FIX A: now called STATE-AGNOSTICALLY  |
//| ONCE at the top of ManageCycle (after the teardown / basket-TP early- |
//| returns, before the rescue-hold / trend-rescue / normal branches) so  |
//| it fires in EVERY state, not just the normal-cycle tail. Because it    |
//| now runs FIRST in a normal cycle (was last in 0.0.40), it may claim    |
//| the one-open-per-tick budget before averaging / the rescue hedge when  |
//| both are eligible - intended circuit-breaker priority.                 |
//+------------------------------------------------------------------+
void EquityHedgeBackstop(const double bid, const double ask)
  {
   // 1. maintain the peak-equity high-water mark (monitoring/logging only).
   UpdatePeakEquity();

   // 2. feature OFF unless an arm threshold is configured.
   if(InpEquityHardFlattenDDUSD <= 0.0)
      return;

   double dd = EquityFloatingDrawdown();          // absolute balance-equity DD

   // 3. hysteresis release: disarm once DD recedes below the release band.
   if(g_equityBackstopArmed && dd <= MoneyInput(InpEquityHardFlattenReleaseDDUSD))
     {
      g_equityBackstopArmed = false;
      Log(1, StringFormat("Straddle: equity backstop disarmed - equity DD %.2f <= release %.2f",
                          dd, MoneyInput(InpEquityHardFlattenReleaseDDUSD)));
     }

   // 4. arm when DD reaches the hard-flatten threshold.
   if(dd >= MoneyInput(InpEquityHardFlattenDDUSD))
     {
      if(!g_equityBackstopArmed)
         Log(1, StringFormat("Straddle: equity backstop ARMED - equity DD %.2f >= flatten %.2f; net-flattening exposure (no realized loss)",
                             dd, MoneyInput(InpEquityHardFlattenDDUSD)));
      g_equityBackstopArmed = true;
     }

   if(!g_equityBackstopArmed)
      return;

   // 5. while armed, open a FULL-net hedge to neutralize exposure. Re-uses the
   //    rescue-hedge account/hedging self-gates and the SAME cooldown stamp so
   //    it cannot double-open with the rescue hedge in the same tick (the
   //    effective cooldown is clamped to [1, 30]s: capped at 30 to allow a fast
   //    top-up after a partial fill during a crash, floored at 1 so the shared
   //    stamp still blocks a same-tick double-open when cooldown is set to 0).
   if(!InpUseRescueHedge || !IsHedgingAccount())
      return;
   if(AccountInfoInteger(ACCOUNT_HEDGE_ALLOWED) == 0)
      return;
   if(InpRescueMaxHedges > 0 && CountRescueHedges() >= InpRescueMaxHedges)
      return;

   int effectiveCooldown = InpRescueHedgeCooldownSec;
   if(effectiveCooldown > 30)
      effectiveCooldown = 30;
   // 0.0.41 FIX A (hardening): floor the LOWER bound at >= 1s. With Inp
   // RescueHedgeCooldownSec==0 the rescue hedge's own cooldown gate (Try
   // OpenRescueHedge, "InpRescueHedgeCooldownSec > 0") is fully disabled, so the
   // shared g_lastRescueHedgeTime stamp would not block a same-tick rescue-hedge
   // open after the backstop just opened (or vice-versa). Flooring the backstop's
   // effective cooldown at 1s makes the just-set stamp block a same-tick reverse-
   // order backstop open even when cooldown==0; the CountRescueHedges cap above is
   // the additional hard backstop against stacking. (OnInit also WARNs on this
   // misconfig.) The 0.0.40 default cooldown=300 is unaffected.
   if(effectiveCooldown < 1)
      effectiveCooldown = 1;
   if(g_lastRescueHedgeTime > 0 &&
      (TimeCurrent() - g_lastRescueHedgeTime) < effectiveCooldown)
      return;

   double buyLots = 0.0, sellLots = 0.0;
   NetExposureLots(buyLots, sellLots);
   double netLots = buyLots - sellLots;
   if(MathAbs(netLots) <= 1.0e-8)
      return;

   // FULL-net hedge: coverFrac forced to 1.0 (desired = |net|), clamped to net
   // and guarded by the SAME RescueHedgeMarginOk / RescueHedgeOrderCheckOk path.
   OpenNetHedge(bid, ask, 1.0, "equity-backstop");
  }

bool TrendRescueCoverage(double &bookedProfit,
                         double &bookedLoss,
                         double &floatingLoss,
                         double &required,
                         double &cycleNet,
                         double &floating)
  {
   TrendRescueSnapshot snapshot;
   GetTrendRescueSnapshot(snapshot);
   bookedProfit = snapshot.bookedProfit;
   bookedLoss = snapshot.bookedLoss;
   floatingLoss = snapshot.floatingLoss;
   required = snapshot.required;
   cycleNet = snapshot.cycleNet;
   floating = snapshot.floating;
   return snapshot.covered;
  }

void LogTrendRescueStatus(const string phase, const bool force)
  {
   if(!g_trendRescueActive && !force)
      return;
   datetime now = TimeCurrent();
   if(!force)
     {
      if(InpRescueStatusLogSeconds <= 0)
         return;
      if(g_lastRescueStatusLog > 0 && (now - g_lastRescueStatusLog) < InpRescueStatusLogSeconds)
         return;
     }
   g_lastRescueStatusLog = now;

   TrendRescueSnapshot snapshot;
   GetTrendRescueSnapshot(snapshot);

   // 1.1.3: throttle identical status lines; state-change (message diff) prints immediately
   LogState(1, "tr-status",
            StringFormat("Straddle: trend rescue status: phase=%s direction=%s bookedProfit=%.2f bookedLoss=%.2f floatingLoss=%.2f required=%.2f coverageGap=%.2f effectiveHarvestTarget=%.2f covered=%s cycleNet=%.2f floating=%.2f anchorTrusted=%s anchorBalance=%.2f positions=%d pendings=%d trendEntries=%d",
                         phase,
                         TrendRescueDirectionName(),
                         snapshot.bookedProfit, snapshot.bookedLoss, snapshot.floatingLoss, snapshot.required,
                         snapshot.coverageGap, snapshot.effectiveHarvestTarget,
                         (snapshot.covered ? "yes" : "no"),
                         snapshot.cycleNet, snapshot.floating,
                         (g_rescueAnchorTrusted ? "yes" : "no"),
                         g_rescueAnchorBalance,
                         snapshot.positions, snapshot.pendings, snapshot.trendEntries),
            60);
  }

bool TrendRescueProfitCoveredReset()
  {
   if(!g_trendRescueActive)
      return false;

   if(!g_cycleStartTrusted || g_cycleStart <= 0)
     {
      Log(1, StringFormat("Straddle: trend rescue reset deferred: reason=cycle_start_untrusted direction=%s - keeping trend rescue active; coverage cannot be proven from an untrusted cycle start marker",
                          TrendRescueDirectionName()));
      return false;
     }

   TrendRescueSnapshot snapshot;
   GetTrendRescueSnapshot(snapshot);
   if(!snapshot.covered)
      return false;

   double closeBuffer = MoneyInput(InpCleanupCostBufferUSD);
   if(snapshot.cycleNet < closeBuffer)
     {
      string msg = StringFormat("Straddle: trend rescue entry skipped: reason=profit_covered_but_cycleNet_guard cycleNet=%.2f closeBuffer=%.2f",
                                snapshot.cycleNet, closeBuffer);
      LogTrendRescueEntrySkip("profit_covered_but_cycleNet_guard", msg, snapshot.coverageGap);
      return false;
     }

   Log(1, StringFormat("Straddle: profit-covered reset: bookedProfit >= bookedLoss + floatingLoss + buffer; closing all and rebuilding (%.2f >= %.2f + %.2f + %.2f, cycleNet %.2f)",
                       snapshot.bookedProfit, snapshot.bookedLoss, snapshot.floatingLoss, closeBuffer, snapshot.cycleNet));
   BeginTearDown(StringFormat("profit-covered reset: trend rescue bookedProfit %.2f >= required %.2f, direction %s, cycleNet %.2f",
                              snapshot.bookedProfit, snapshot.required, TrendRescueDirectionName(), snapshot.cycleNet),
                 closeBuffer);
   return true;
  }

bool TrendRescueMarginOk(const bool isBuy,
                         const double lots,
                         const double price,
                         const double coverageGap)
  {
   double accountMargin = AccountInfoDouble(ACCOUNT_MARGIN);
   double marginLevel = AccountInfoDouble(ACCOUNT_MARGIN_LEVEL);
   if(InpTrendRescueMinMarginLevelPct > 0.0 && accountMargin > 0.0)
     {
       if(marginLevel <= 0.0 || marginLevel < InpTrendRescueMinMarginLevelPct)
          {
           LogTrendRescueEntrySkip("margin_level",
               StringFormat("Straddle: trend rescue entry skipped: reason=margin_level %.2f%% below %.2f%% coverageGap=%.2f",
                            marginLevel, InpTrendRescueMinMarginLevelPct, coverageGap),
               coverageGap);
          return false;
          }
      }

   double requiredMargin = 0.0;
   ENUM_ORDER_TYPE orderType = (isBuy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL);
   if(!OrderCalcMargin(orderType, g_sym, lots, price, requiredMargin))
     {
      LogOp(StringFormat("Straddle: trend rescue entry skipped: reason=OrderCalcMargin_failed side=%s lots=%.2f",
                         (isBuy ? "BUY" : "SELL"), lots));
      return false;
     }

   double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
   double closeBuffer = MoneyInput(InpCleanupCostBufferUSD);
   if(requiredMargin <= 0.0 || freeMargin <= requiredMargin + closeBuffer)
     {
      LogTrendRescueEntrySkip("free_margin",
          StringFormat("Straddle: trend rescue entry skipped: reason=free_margin freeMargin=%.2f required=%.2f buffer=%.2f coverageGap=%.2f",
                       freeMargin, requiredMargin, closeBuffer, coverageGap),
          coverageGap);
      return false;
     }

   return true;
  }

double ExpectedMoveMoney(const bool isBuy,
                         const double lots,
                         const double priceMove)
  {
   if(lots <= 0.0 || priceMove <= 0.0)
      return 0.0;

   double bid = 0.0, ask = 0.0;
   if(!RefreshCurrentPrices(bid, ask))
     {
      bid = SymbolInfoDouble(g_sym, SYMBOL_BID);
      ask = SymbolInfoDouble(g_sym, SYMBOL_ASK);
     }

   double openPrice = (isBuy ? ask : bid);
   if(openPrice <= 0.0)
      openPrice = CurrentMidPrice();
   if(openPrice <= 0.0)
      return 0.0;

   double closePrice = (isBuy ? openPrice + priceMove : openPrice - priceMove);
   double profit = 0.0;
   ENUM_ORDER_TYPE orderType = (isBuy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL);
   if(OrderCalcProfit(orderType, g_sym, lots, openPrice, closePrice, profit))
      return NormalizeAccountMoney(MathMax(0.0, profit));

   double tick = TickSize();
   double tickValue = SymbolInfoDouble(g_sym, SYMBOL_TRADE_TICK_VALUE);
   if(tick <= 0.0 || tickValue <= 0.0)
      return 0.0;

   double ticks = priceMove / tick;
   if(ticks <= 0.0)
      return 0.0;
   return NormalizeAccountMoney(ticks * tickValue * lots);
  }

bool TrendRescueEntryImpact(const bool isBuy,
                            const double lots,
                            double &expectedFunding,
                            double &coverageGap,
                            double &improvementPct,
                            double &expectedMove)
  {
   expectedFunding = 0.0;
   coverageGap = TrendRescueCoverageGap();
   improvementPct = 0.0;
   expectedMove = 0.0;
   if(lots <= 0.0)
      return false;

   expectedMove = NormalizePriceDistanceToTick(ATRPriceDistance() *
                                               MathMax(0.0, InpNoEffectExpectedMoveATRShare));
   if(expectedMove <= 0.0)
      expectedMove = TickSize();
   expectedFunding = ExpectedMoveMoney(isBuy, lots, expectedMove);

   if(coverageGap > 0.0)
      improvementPct = (expectedFunding / coverageGap) * 100.0;
   else if(expectedFunding > 0.0)
      improvementPct = 100.0;
   return true;
  }

bool TrendRescueEntryHasEffect(const bool isBuy,
                               const double lots,
                               const int currentDirectionEntries,
                               const int totalEntries,
                               const double losingSideLots,
                               const double activeTrendLots,
                               bool &impactProven,
                               double &expectedFunding,
                               double &impactCoverageGap,
                               double &improvementPct,
                               double &expectedMove)
  {
   impactProven = false;
   expectedFunding = 0.0;
   impactCoverageGap = 0.0;
   improvementPct = 0.0;
   expectedMove = 0.0;

   if(!TrendRescueEntryImpact(isBuy, lots, expectedFunding,
                              impactCoverageGap, improvementPct, expectedMove))
     {
      if(ShouldPrepareTrendRescueEntrySkipLog("no_effect_guard", impactCoverageGap))
         EmitTrendRescueEntrySkipPrepared("no_effect_guard",
             StringFormat("Straddle: trend rescue entry skipped: reason=no_effect_guard side=%s lots=%.2f expectedMove=%.2f expectedFunding=%.2f coverageGap=%.2f improvementPct=%.2f minImprovePct=%.2f minCleanupFunding=%.2f impact=invalid",
                          (isBuy ? "BUY" : "SELL"), lots, expectedMove,
                          expectedFunding, impactCoverageGap, improvementPct,
                          InpNoEffectMinCoverageImprovePct,
                          MoneyInput(InpNoEffectMinCleanupFundingUSD)),
             impactCoverageGap);
      return false;
     }

   if(!InpUseTrendRescueNoEffectGuard)
     {
      impactProven = true;
      return true;
     }

   if(InpNoEffectAllowIfNoPositions && CountMyPositions() <= 0)
      return true;

   bool firstEntryException = ((totalEntries <= 0 || currentDirectionEntries <= 0) &&
                               losingSideLots > 0.0 &&
                               activeTrendLots <= 1.0e-8);
   if(firstEntryException)
      return true;

   bool improvesCoverage = (improvementPct + 1.0e-8 >= InpNoEffectMinCoverageImprovePct);
   bool fundsCleanup = (expectedFunding + 1.0e-8 >= MoneyInput(InpNoEffectMinCleanupFundingUSD));
   if(improvesCoverage || fundsCleanup)
     {
      impactProven = true;
      return true;
     }

   if(ShouldPrepareTrendRescueEntrySkipLog("no_effect_guard", impactCoverageGap))
      EmitTrendRescueEntrySkipPrepared("no_effect_guard",
          StringFormat("Straddle: trend rescue entry skipped: reason=no_effect_guard side=%s lots=%.2f expectedMove=%s expectedFunding=%.2f coverageGap=%.2f improvementPct=%.2f minImprovePct=%.2f minCleanupFunding=%.2f losingSideLots=%.2f activeTrendLots=%.2f currentDirectionEntries=%d totalEntries=%d",
                       (isBuy ? "BUY" : "SELL"),
                       lots,
                       DoubleToString(expectedMove, g_digits),
                       expectedFunding,
                       impactCoverageGap,
                       improvementPct,
                       InpNoEffectMinCoverageImprovePct,
                       MoneyInput(InpNoEffectMinCleanupFundingUSD),
                       losingSideLots,
                       activeTrendLots,
                       currentDirectionEntries,
                       totalEntries),
          impactCoverageGap);
   return false;
  }

double TrendRescueExposureLotsForSide(const bool isBuy)
  {
   EnsureBookAggregates();
   return (isBuy ? g_bookCache.trendBuySideLots : g_bookCache.trendSellSideLots);
  }

double StaleOldGridExposureLotsForSide(const bool isBuy)
  {
   EnsureBookAggregates();
   return (isBuy ? g_bookCache.staleBuyLots : g_bookCache.staleSellLots);
  }

bool TrendRescueOppositeExposureGuardAllows(const bool isBuy,
                                            const double lots,
                                            const bool impactProven,
                                            const double coverageGap)
  {
   if(!InpUseTrendRescueOppositeExposureGuard)
      return true;

   double oppositeLots = TrendRescueExposureLotsForSide(!isBuy);
   double maxOppositeLots=g_normalizedOppositeExposureLotCap;

   // 0.0.33: HARD opposite-exposure backstop. This must be evaluated BEFORE
   // the soft-cap pass-through, the impactProven bypass, and the net-reduce
   // escape so that it DOMINATES every soft-tier escape. When the soft cap is
   // 0/disabled the auto-backstop stays 0 = DISABLED, so this never refuses a
   // trade in that config (avoids a refuse-to-trade regression). The default
   // auto ceiling (3x soft cap) is generous and must not turn normal recovery
   // into a refusal.
   double effectiveHardMax = (g_normalizedOppositeExposureHardLotCap > 0.0)
                             ? g_normalizedOppositeExposureHardLotCap
                             : (maxOppositeLots > 0.0 ? maxOppositeLots * 3.0 : 0.0);
   if(effectiveHardMax > 0.0 && oppositeLots > effectiveHardMax + 1.0e-8)
     {
      if(ShouldPrepareTrendRescueEntrySkipLog("opposite_str_exposure_hardcap", coverageGap))
         EmitTrendRescueEntrySkipPrepared("opposite_str_exposure_hardcap",
             StringFormat("Straddle: trend rescue entry skipped: reason=opposite_str_exposure_hardcap side=%s lots=%.2f oppositeStrLots=%.2f hardMaxOppositeStrLots=%.2f softMaxOppositeStrLots=%.2f impactProven=%s coverageGap=%.2f",
                          (isBuy ? "BUY" : "SELL"),
                          lots,
                          oppositeLots,
                          effectiveHardMax,
                          maxOppositeLots,
                          (impactProven ? "true" : "false"),
                          coverageGap),
             coverageGap);
      return false;
     }

   if(oppositeLots <= maxOppositeLots + 1.0e-8)
      return true;
   // Intentional soft-tier bypass: a proven coverage/cleanup impact may exceed
   // the soft cap. Now bounded above by the hard ceiling checked just above.
   if(impactProven)
      return true;
   if(!InpOppositeExposureRequireNetReduce)
      return true;

   // Intentional net-reduce escape: allow an offsetting opposite entry when it
   // does not exceed the stale OLD losing grid exposure on that side
   // (StaleOldGridExposureLotsForSide), i.e. it nets down the legacy loser
   // rather than building fresh opposite exposure. Still bounded by the hard
   // ceiling checked above.
   double staleOldLotsReduced = StaleOldGridExposureLotsForSide(!isBuy);
   bool netReducesStaleOldExposure = (staleOldLotsReduced > 0.0 &&
                                      lots <= staleOldLotsReduced + 1.0e-8);
   if(netReducesStaleOldExposure)
      return true;

   if(ShouldPrepareTrendRescueEntrySkipLog("opposite_str_exposure_guard", coverageGap))
      EmitTrendRescueEntrySkipPrepared("opposite_str_exposure_guard",
          StringFormat("Straddle: trend rescue entry skipped: reason=opposite_str_exposure_guard side=%s lots=%.2f oppositeStrLots=%.2f maxOppositeStrLots=%.2f staleOldOppositeLots=%.2f impactProven=%s requireNetReduce=%s coverageGap=%.2f",
                       (isBuy ? "BUY" : "SELL"),
                       lots,
                       oppositeLots,
                       maxOppositeLots,
                       staleOldLotsReduced,
                       (impactProven ? "true" : "false"),
                       (InpOppositeExposureRequireNetReduce ? "true" : "false"),
                       coverageGap),
          coverageGap);
   return false;
  }

bool TrendRescueLots(const int currentDirectionEntries,
                     const int totalEntries,
                     const int currentDirectionEntryCap,
                     const bool isBuy,
                     double &nextEntryLot,
                     double &losingSideLots,
                     double &activeTrendLots,
                     double &targetTrendLots,
                     double &missingTrendLots,
                     const double coverageGap)
  {
   nextEntryLot = 0.0;
   CollectTrendRescueSizingContext(isBuy, losingSideLots, activeTrendLots);

   double vmax=SymbolInfoDouble(g_sym,SYMBOL_VOLUME_MAX);
   if(!MathIsValidNumber(vmax) || vmax<=0.0)
      vmax=NormalizeLot(InpTrendRescueLot);
   double maxEntryLot=(g_normalizedTrendRescueEntryLotCap>0.0
                       ? g_normalizedTrendRescueEntryLotCap
                       : vmax);
   if(vmax > 0.0 && maxEntryLot > vmax)
      maxEntryLot = vmax;
   double equityDD = EquityFloatingDrawdown();
   double equityLotMultiplier = EquityPressureLotMultiplier(equityDD);
   if(equityLotMultiplier < 1.0)
      maxEntryLot = NormalizeDouble(maxEntryLot * equityLotMultiplier, 8);

   int remainingSlots = currentDirectionEntryCap - currentDirectionEntries;
   if(remainingSlots < 0)
      remainingSlots = 0;

   int safetyRemainingSlots = remainingSlots;
   if(InpTrendRescueTotalSafetyMaxEntries > 0)
     {
      safetyRemainingSlots = InpTrendRescueTotalSafetyMaxEntries - totalEntries;
      if(safetyRemainingSlots < 0)
         safetyRemainingSlots = 0;
      if(safetyRemainingSlots < remainingSlots)
        {
         string msg = StringFormat("Straddle: trend rescue pressure target clipped: reason=pressure_target_total_safety_cap totalEntries=%d totalSafetyMax=%d currentDirectionEntries=%d currentDirectionMax=%d remainingSlots=%d safetyRemainingSlots=%d coverageGap=%.2f",
                                   totalEntries, InpTrendRescueTotalSafetyMaxEntries,
                                   currentDirectionEntries, currentDirectionEntryCap,
                                   remainingSlots, safetyRemainingSlots, coverageGap);
         LogTrendRescueEntrySkip("pressure_target_total_safety_cap", msg, coverageGap);
         remainingSlots = safetyRemainingSlots;
        }
     }

   if(remainingSlots <= 0)
     {
      string msg = StringFormat("Straddle: trend rescue entry skipped: reason=max_entries currentDirectionEntries=%d currentDirectionMax=%d totalEntries=%d totalSafetyMax=%d coverageGap=%.2f",
                                currentDirectionEntries, currentDirectionEntryCap,
                                totalEntries, InpTrendRescueTotalSafetyMaxEntries, coverageGap);
      LogTrendRescueEntrySkip("max_entries", msg, coverageGap);
      return false;
     }

   bool pressureTarget = TrendRescueCoveragePressureActive(coverageGap);
   targetTrendLots = losingSideLots * MathMax(0.0, InpTrendRescueExposureRatio);
   bool pressureSlotClipped = false;
   double pressureTrendLots = TrendRescuePressureTargetLots(losingSideLots, activeTrendLots,
                                                            remainingSlots, maxEntryLot, coverageGap,
                                                            pressureSlotClipped);
   if(pressureTrendLots > targetTrendLots)
      targetTrendLots = pressureTrendLots;
   if(pressureTarget && pressureSlotClipped)
      LogTrendRescueEntrySkip("pressure_target_slot_clip",
          StringFormat("Straddle: trend rescue pressure target clipped: reason=pressure_target_slot_clip direction=%s targetTrendLots=%.2f activeTrendLots=%.2f currentDirectionEntries=%d currentDirectionMax=%d remainingSlots=%d maxEntryLot=%.2f coverageGap=%.2f moneyGapLotStep=%.2f",
                       TrendRescueDirectionName(), targetTrendLots, activeTrendLots,
                       currentDirectionEntries, currentDirectionEntryCap,
                       remainingSlots, maxEntryLot, coverageGap,
                       MoneyInput(InpTrendRescueMoneyGapLotStepUSD)),
          coverageGap);
   missingTrendLots = MathMax(0.0, targetTrendLots - activeTrendLots);

   if(InpUseAdaptiveTrendRescueSizing)
      {
       if(missingTrendLots <= 1.0e-8)
         {
           if(ShouldPrepareTrendRescueEntrySkipLog("adaptive_target_reached", coverageGap))
              EmitTrendRescueEntrySkipPrepared("adaptive_target_reached",
                  StringFormat("Straddle: trend rescue entry skipped: reason=adaptive_target_reached direction=%s losingSideLots=%.2f activeTrendLots=%.2f targetTrendLots=%.2f missingTrendLots=%.2f nextEntryLot=0.00 coverageGap=%.2f pressureTarget=%s",
                               TrendRescueDirectionName(), losingSideLots, activeTrendLots,
                               targetTrendLots, missingTrendLots, coverageGap,
                               (pressureTarget ? "true" : "false")),
                  coverageGap);
          return false;
          }

       double vmin = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
        if(vmin > 0.0 && maxEntryLot + 1.0e-8 < vmin)
          {
           LogTrendRescueEntrySkip("adaptive_lot_min_exceeds_cap",
               StringFormat("Straddle: trend rescue entry skipped: reason=adaptive_lot_min_exceeds_cap brokerMin=%.2f cap=%.2f losingSideLots=%.2f activeTrendLots=%.2f targetTrendLots=%.2f missingTrendLots=%.2f nextEntryLot=0.00 coverageGap=%.2f",
                            vmin, maxEntryLot, losingSideLots, activeTrendLots,
                            targetTrendLots, missingTrendLots, coverageGap),
               coverageGap);
          return false;
          }

      double desiredLots = MathMax(InpTrendRescueLot, missingTrendLots / (double)remainingSlots);
      if(equityLotMultiplier < 1.0)
         desiredLots = NormalizeDouble(desiredLots * equityLotMultiplier, 8);
      if(pressureTarget && desiredLots > maxEntryLot + 1.0e-8)
         LogTrendRescueEntrySkip("pressure_target_lot_clip",
             StringFormat("Straddle: trend rescue pressure target clipped: reason=pressure_target_lot_clip direction=%s desiredLots=%.2f maxEntryLot=%.2f targetTrendLots=%.2f missingTrendLots=%.2f remainingSlots=%d coverageGap=%.2f",
                          TrendRescueDirectionName(), desiredLots, maxEntryLot,
                          targetTrendLots, missingTrendLots, remainingSlots, coverageGap),
             coverageGap);
      nextEntryLot = NormalizeTrendRescueEntryLot(desiredLots, maxEntryLot, true);
      if(nextEntryLot <= 0.0)
         {
          LogTrendRescueEntrySkip("adaptive_lot_too_small",
              StringFormat("Straddle: trend rescue entry skipped: reason=adaptive_lot_too_small requested=%.2f cap=%.2f losingSideLots=%.2f activeTrendLots=%.2f targetTrendLots=%.2f missingTrendLots=%.2f nextEntryLot=0.00 coverageGap=%.2f",
                           desiredLots, maxEntryLot, losingSideLots, activeTrendLots,
                           targetTrendLots, missingTrendLots, coverageGap),
              coverageGap);
          return false;
          }
     }
   else
     {
       double requestedLots = InpTrendRescueLot;
       if(equityLotMultiplier < 1.0)
          requestedLots = NormalizeDouble(requestedLots * equityLotMultiplier, 8);
       nextEntryLot = NormalizeTrendRescueEntryLot(requestedLots, maxEntryLot, false);
       if(nextEntryLot <= 0.0)
          {
          LogTrendRescueEntrySkip("lot_too_small",
              StringFormat("Straddle: trend rescue entry skipped: reason=lot_too_small requested=%.2f cap=%.2f losingSideLots=%.2f activeTrendLots=%.2f targetTrendLots=%.2f missingTrendLots=%.2f nextEntryLot=0.00 coverageGap=%.2f",
                           requestedLots, maxEntryLot, losingSideLots, activeTrendLots,
                           targetTrendLots, missingTrendLots, coverageGap),
              coverageGap);
          return false;
          }
     }

   LogFmt(2, StringFormat("Straddle: trend rescue sizing: direction=%s losingSideLots=%.2f activeTrendLots=%.2f targetTrendLots=%.2f missingTrendLots=%.2f remainingSlots=%d nextEntryLot=%.2f adaptive=%s cap=%.2f pressureTarget=%s equityPressure=%s equitySevere=%s equityDD=%.2f lotMultiplier=%.2f pressureGap=%.2f pressureRatio=%.2f moneyGapLotStep=%.2f currentDirectionEntries=%d totalEntries=%d totalSafetyMax=%d",
                        TrendRescueDirectionName(), losingSideLots, activeTrendLots,
                        targetTrendLots, missingTrendLots, remainingSlots, nextEntryLot,
                        (InpUseAdaptiveTrendRescueSizing ? "true" : "false"), maxEntryLot,
                        (pressureTarget ? "true" : "false"),
                        (EquityPressureActive(equityDD) ? "true" : "false"),
                        (EquityPressureSevere(equityDD) ? "true" : "false"),
                        equityDD, equityLotMultiplier,
                        MoneyInput(InpTrendRescuePressureGapUSD),
                        InpTrendRescuePressureExposureRatio,
                        MoneyInput(InpTrendRescueMoneyGapLotStepUSD),
                        currentDirectionEntries, totalEntries,
                        InpTrendRescueTotalSafetyMaxEntries));
   return true;
  }

bool TrendRescueStepReferencePrice(const bool isBuy,
                                   double &referencePrice,
                                   int &sameDirectionEntries)
  {
   referencePrice = 0.0;
   sameDirectionEntries = 0;
   ENUM_POSITION_TYPE wantedType = (isBuy ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(!IsTrendRescueComment(PositionGetString(POSITION_COMMENT)))
         continue;

      ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      if(type != wantedType)
         continue;

      double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      if(openPrice <= 0.0)
         continue;

      sameDirectionEntries++;
      if(referencePrice <= 0.0)
         referencePrice = openPrice;
      else if(isBuy)
         referencePrice = MathMax(referencePrice, openPrice);
      else
         referencePrice = MathMin(referencePrice, openPrice);
     }

   return (sameDirectionEntries > 0 && referencePrice > 0.0);
  }

bool TrendRescueStepReady(const bool isBuy,
                          const double price,
                          const double effectiveStep,
                          double &referencePrice,
                          int &sameDirectionEntries)
  {
   if(effectiveStep <= 0.0)
      return true;
   if(!TrendRescueStepReferencePrice(isBuy, referencePrice, sameDirectionEntries))
      return true;
   if(sameDirectionEntries <= 0)
      return true;
   if(isBuy)
      return (price >= referencePrice + effectiveStep);
    return (price <= referencePrice - effectiveStep);
  }

bool TrendRescueDirectionStillConfirmed(const bool isBuy,
                                        const double bid,
                                        const double ask)
  {
   if(!g_trendRescueActive)
      return false;
   if(g_trendRescueDirection == 1 && !isBuy)
      return false;
   if(g_trendRescueDirection == -1 && isBuy)
      return false;
   if(g_trendRescueDirection != 1 && g_trendRescueDirection != -1)
      return false;
   if(bid <= 0.0 || ask <= 0.0)
      return false;

   int lookbackBars = InpRecoveryDirectionLookbackBars;
   if(lookbackBars < 1)
      lookbackBars = 1;
   double minMove = MathMax(0.0, PriceDistanceInput(InpRecoveryDirectionMinMoveUSD));
   if(minMove <= 0.0)
      return false;

   double lookbackClose = iClose(g_sym, PERIOD_M1, lookbackBars);
   if(lookbackClose <= 0.0)
      return false;

   double mid = (bid + ask) * 0.5;
   double move = mid - lookbackClose;
   if(g_trendRescueDirection == 1)
      return (move >= minMove);
   if(g_trendRescueDirection == -1)
      return (move <= -minMove);
   return false;
  }

bool TrendRescuePressureDirectionConfirmed(const bool isBuy,
                                           const double bid,
                                           const double ask)
  {
   if(!g_trendRescueActive)
      return false;
   if(g_trendRescueDirection == 1 && !isBuy)
      return false;
   if(g_trendRescueDirection == -1 && isBuy)
      return false;
   if(g_trendRescueDirection != 1 && g_trendRescueDirection != -1)
      return false;
   if(bid <= 0.0 || ask <= 0.0)
      return false;

   int lookbackBars = InpTrendRescuePressureConfirmLookbackBars;
   if(lookbackBars < 1)
      lookbackBars = 1;
   double minMove = MathMax(0.0, PriceDistanceInput(InpTrendRescuePressureConfirmMoveUSD));
   if(minMove <= 0.0)
      return true;

   double lookbackClose = iClose(g_sym, PERIOD_M1, lookbackBars);
   if(lookbackClose <= 0.0)
      return false;

   double mid = (bid + ask) * 0.5;
   double move = mid - lookbackClose;
   if(g_trendRescueDirection == 1)
      return (move >= minMove);
   if(g_trendRescueDirection == -1)
      return (move <= -minMove);
   return false;
  }

bool TrendRescueContinuationOverrideAllowed(const bool isBuy,
                                            const double coverageGap,
                                            const int sameDirectionEntries,
                                            const double bid,
                                            const double ask,
                                            string &overrideReason)
  {
   overrideReason = "";
   if(!InpUseTrendRescueContinuationPressureOverride)
      return false;
   if(!g_trendRescueActive)
      return false;
   if(InpTrendRescuePressureBypassStepMaxEntries < 0)
      return false;
   bool normalPressureActive = TrendRescueCoveragePressureActive(coverageGap);
   bool stuckPressureActive = (coverageGap >= MoneyInput(InpStuckRecoveryGapUSD));
   if(!normalPressureActive && !stuckPressureActive)
      return false;
   double equityDD = EquityFloatingDrawdown();
   if(InpEquityPressureDisableContinuationOverride && EquityPressureSevere(equityDD))
     {
      LogTrendRescueEntrySkip("equity_pressure_continuation_override_disabled",
          StringFormat("Straddle: trend rescue entry gate bypass blocked: reason=equity_pressure_continuation_override_disabled equityDD=%.2f severeThreshold=%.2f coverageGap=%.2f sameDirectionEntries=%d",
                       equityDD, MoneyInput(InpEquityPressureSevereDDUSD),
                       coverageGap, sameDirectionEntries),
          coverageGap);
      return false;
     }
   if(!TrendRescuePressureDirectionConfirmed(isBuy, bid, ask))
      return false;

   if(stuckPressureActive)
     {
      overrideReason = "coverage_pressure_continuation_override";
      return true;
     }

   if(sameDirectionEntries > InpTrendRescuePressureBypassStepMaxEntries)
      return false;

   overrideReason = "continuation_pressure_override";
   return true;
  }

bool UpdateRollingTrendRescueDirection(const double bid, const double ask)
  {
   if(!g_trendRescueActive || !InpUseRollingRecoveryDirection)
      return false;
   if(g_trendRescueDirection != 1 && g_trendRescueDirection != -1)
      return false;
   if(bid <= 0.0 || ask <= 0.0)
      return false;

   int lookbackBars = InpRecoveryDirectionLookbackBars;
   if(lookbackBars < 1)
      lookbackBars = 1;
   double minMove = MathMax(0.0, PriceDistanceInput(InpRecoveryDirectionMinMoveUSD));
   if(minMove <= 0.0)
      return false;
   if(InpRecoveryDirectionSwitchCooldownSec > 0 &&
      g_lastTrendRescueDirectionSwitchTime > 0 &&
      (TimeCurrent() - g_lastTrendRescueDirectionSwitchTime) < InpRecoveryDirectionSwitchCooldownSec)
      return false;

   double lookbackClose = iClose(g_sym, PERIOD_M1, lookbackBars);
   if(lookbackClose <= 0.0)
      return false;

   double mid = (bid + ask) * 0.5;
   double move = mid - lookbackClose;
   int confirmedDirection = 0;
   if(move >= minMove)
      confirmedDirection = 1;
   else if(move <= -minMove)
      confirmedDirection = -1;
   else
      return false;

   if(confirmedDirection == g_trendRescueDirection)
      return false;

   int previousDirection = g_trendRescueDirection;
   double staleLastEntryPrice = g_trendRescueLastEntryPrice;
   datetime staleLastEntryTime = g_lastTrendRescueEntryTime;
   g_trendRescueDirection = confirmedDirection;
   g_lastTrendRescueDirectionSwitchTime = TimeCurrent();
   bool resetEntryGate = false;
   bool resetPriceGate = false;
   if(InpRecoveryDirectionResetEntryGate)
     {
      g_lastTrendRescueEntryTime = 0;
      resetEntryGate = true;
     }
   if(g_trendRescueLastEntryPrice > 0.0)
     {
      g_trendRescueLastEntryPrice = 0.0;
      resetPriceGate = true;
     }
   PersistTrendRescueState();
   Log(1, StringFormat("Straddle: trend rescue rolling direction switch: from=%s to=%s move=%.2f minMove=%.2f lookbackBars=%d lookbackClose=%s mid=%s cooldown=%d entryGateReset=%s priceGateReset=%s staleLast=%s staleLastTime=%I64d",
                       (previousDirection > 0 ? "BUY" : "SELL"),
                       TrendRescueDirectionName(), move, minMove, lookbackBars,
                       DoubleToString(lookbackClose, g_digits),
                       DoubleToString(mid, g_digits),
                       InpRecoveryDirectionSwitchCooldownSec,
                       (resetEntryGate ? "true" : "false"),
                       (resetPriceGate ? "true" : "false"),
                       DoubleToString(staleLastEntryPrice, g_digits),
                       (long)staleLastEntryTime));
   return true;
  }

//+------------------------------------------------------------------+
//| 0.0.39 BASKET AVERAGING (bounded averaging-down recovery).        |
//| Builds a market DEAL request for one same-side averaging add,     |
//| tagged STR AVB/AVS so it folds into the comment-agnostic basket-  |
//| TP close and is excluded from every realized-loss cleanup lane.   |
//| Mirrors BuildTrendRescueEntryRequest exactly (SL=0/TP=0, magic,   |
//| filling, deviation, GTC) except for the comment tag.              |
//+------------------------------------------------------------------+
//+------------------------------------------------------------------+
//| 0.0.39 Projected margin gate for an averaging add - SAME          |
//| philosophy as TrendRescueMarginOk (margin-level floor + projected |
//| OrderCalcMargin vs free margin minus the cost buffer). Stricter   |
//| default floor (InpAvgMinMarginLevelPct, 500%) because averaging   |
//| is counter-trend. Returns false (skip the add) on ANY failure.    |
//+------------------------------------------------------------------+
bool BasketAveragingMarginOk(const bool isBuy,
                             const double lots,
                             const double price)
  {
   double accountMargin = AccountInfoDouble(ACCOUNT_MARGIN);
   double marginLevel   = AccountInfoDouble(ACCOUNT_MARGIN_LEVEL);
   if(InpAvgMinMarginLevelPct > 0.0 && accountMargin > 0.0)
     {
      if(marginLevel <= 0.0 || marginLevel < InpAvgMinMarginLevelPct)
        {
         Log(2, StringFormat("Straddle: averaging entry skipped: reason=margin_level %.2f%% below %.2f%%",
                             marginLevel, InpAvgMinMarginLevelPct));
         return false;
        }
     }

   double requiredMargin = 0.0;
   ENUM_ORDER_TYPE orderType = (isBuy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL);
   if(!OrderCalcMargin(orderType, g_sym, lots, price, requiredMargin))
     {
      LogOp(StringFormat("Straddle: averaging entry skipped: reason=OrderCalcMargin_failed side=%s lots=%.2f",
                         (isBuy ? "BUY" : "SELL"), lots));
      return false;
     }

   double freeMargin  = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
   double closeBuffer = MoneyInput(InpCleanupCostBufferUSD);
   if(requiredMargin <= 0.0 || freeMargin <= requiredMargin + closeBuffer)
     {
      Log(2, StringFormat("Straddle: averaging entry skipped: reason=free_margin freeMargin=%.2f required=%.2f buffer=%.2f",
                         freeMargin, requiredMargin, closeBuffer));
      return false;
     }

   return true;
  }

//+------------------------------------------------------------------+
//| 0.0.39 BOUNDED AVERAGING-DOWN ENGINE. Opens at most ONE same-side |
//| market add on the buried side per accepted call; never closes,    |
//| never cuts, never realizes a loss. The existing comment-agnostic  |
//| basket-TP (CheckBasketTakeProfit) is the sole close path - it     |
//| banks the whole book (grid + AVG adds + hedges) green the instant |
//| total floating >= +InpBasketTakeProfitUSD.                        |
//|                                                                    |
//| HARD CAPS (both recomputed LIVE from positions, restart-robust,   |
//| re-checked inside the send loop): InpAvgMaxEntries (count) and    |
//| InpAvgMaxLots (total lots = the absolute blow-up bound). Once     |
//| either cap is reached the pile is FROZEN - returns false every    |
//| tick, no further adds, no close.                                  |
//|                                                                    |
//| Mutually exclusive with the ENTIRE hedge/rescue/backstop family   |
//| (trend rescue, rescue hold, teardown, equity backstop, rescue/    |
//| stuck-exposure hedge) AND shares the g_lastRescueHedgeTime one-    |
//| open-per-tick stamp so at most one market order opens per tick    |
//| across all engines.                                               |
//+------------------------------------------------------------------+
bool TryBasketAveragingEntry(const double bid, const double ask)
  {
   // (1) master switch - default OFF reproduces 0.0.38 bit-for-bit.
   if(!InpUseBasketAveraging)
      return false;
   // (2) half-configured setfile = OFF: any zero sub-gate disables the engine
   //     so it can NEVER run unbounded.
   if(InpAvgTriggerLossUSD<=0.0 || InpAvgMaxEntries<=0 || g_normalizedAvgLotCap<=0.0)
      return false;
   // (3) MUTUAL EXCLUSION with the entire hedge/rescue/backstop family. The
   //     wiring already guarantees this tail is unreachable while trend rescue
   //     / rescue hold / teardown are active; the explicit guard is belt-and-
   //     suspenders AND additionally blocks the equity backstop and the
   //     rescue/stuck-exposure hedge that DO live in this same tail, so the two
   //     can never open opposing orders in the same tick.
   if(g_trendRescueActive || g_rescueHolding || g_tearingDown ||
      g_equityBackstopArmed || RescueHedgeStateActive() || StuckExposureHedgeEligible())
      return false;
   // (4) tradeability / price sanity.
   if(!CanTrade() || bid <= 0.0 || ask <= 0.0)
      return false;
   // (5) SHARED one-open-per-tick stamp: if a rescue hedge / backstop opened
   //     recently, do not also open an averaging add this window (and vice
   //     versa via the stamp we set on success below). Bounded by InpAvgCooldownSec.
   if(InpAvgCooldownSec > 0 && g_lastRescueHedgeTime > 0 &&
      (TimeCurrent() - g_lastRescueHedgeTime) < InpAvgCooldownSec)
      return false;

   // (6) buried side from GRID + STR AV legs ONLY (hedge / trend legs excluded
   //     so an open opposite-side hedge cannot misdirect or flip the side).
   double buyLots = 0.0, sellLots = 0.0;
   AveragingCoreExposureLots(buyLots, sellLots);
   if(MathAbs(buyLots - sellLots) <= 1.0e-8)
      return false;                                  // balanced/flat -> nothing buried
   // 0.0.41 FIX C2: pick the side with the MOST-NEGATIVE floating (deepest USD
   // loss) as the truly buried pile, instead of the larger-lot side. On a lot-
   // imbalanced book where the larger-lot side is NOT the deepest-loss side this
   // now adds to the genuinely buried side. The 9668 balanced-flat guard still
   // uses LOTS to confirm a real two-sided imbalance exists first, so a perfectly
   // lot-balanced book is still treated as "nothing buried" (parity with 0.0.40).
   double buyFloating  = AveragingCoreSideFloating(true);
   double sellFloating = AveragingCoreSideFloating(false);
   bool isBuy = (buyFloating < sellFloating);         // deepest-loss (most negative) side = truly buried

   // (7) confirm the buried side is actually underwater past the trigger.
   double buriedFloating = AveragingCoreSideFloating(isBuy);
   double trigger = MoneyInput(InpAvgTriggerLossUSD);
   if(buriedFloating > -trigger)
      return false;                                  // not buried enough yet

   // (8) HARD CAP - count. Recomputed live; freeze silently once reached.
   int adds = CountAveragingEntries();
   if(InpAvgMaxEntries > 0 && adds >= InpAvgMaxEntries)
     {
      Log(2, StringFormat("Straddle: averaging entry skipped: reason=max_entries adds=%d max=%d",
                         adds, InpAvgMaxEntries));
      return false;
     }

   // (9) per-add lot: flat sizing (no martingale lot scaling). InpAvgLot=0 ->
   //     grid near-lot. If the lot exceeds remaining cap room, REFUSE (freeze)
   //     - never silently downsize below broker vmin.
   double rawLot  = (InpAvgLot > 0.0
                     ? InpAvgLot
                     : (IsAutoLotMode() ? InpAutoLotNear * AutoLotScaleFactor()
                                        : InpLotNear));
   double nextLot = NormalizeLot(rawLot);
   if(nextLot <= 0.0)
      return false;
   double totalAvgLots = AveragingTotalLots();
   if(g_normalizedAvgLotCap>0.0 && totalAvgLots+nextLot>g_normalizedAvgLotCap+1.0e-8)
     {
      Log(2, StringFormat("Straddle: averaging entry skipped: reason=max_lots total=%.2f next=%.2f max=%.2f",
                         totalAvgLots,nextLot,g_normalizedAvgLotCap));
      return false;
     }

   double price = (isBuy ? ask : bid);

   // (10) ADVERSE-step gate: require a meaningful adverse move since the last
   //      add. Reference = g_basketAvgLastEntryPrice if known, else the buried
   //      side's worst open price (degrades gracefully after a restart). For
   //      buried longs require price <= reference-step (price fell further);
   //      for buried shorts require price >= reference+step (price rose
   //      further). First-ever add (no reference) is allowed immediately.
   double step = PriceDistanceInput(InpAvgStepUSD);
   double reference = g_basketAvgLastEntryPrice;
   if(reference <= 0.0)
      reference = AveragingCoreWorstOpenPrice(isBuy);
   bool firstAdd = (reference <= 0.0);
   if(!firstAdd && step > 0.0)
     {
      bool adverseOk = (isBuy ? (price <= reference - step)
                              : (price >= reference + step));
      if(!adverseOk)
        {
         Log(2, StringFormat("Straddle: averaging entry skipped: reason=adverse_step side=%s price=%s reference=%s step=%s",
                            (isBuy ? "BUY" : "SELL"),
                            DoubleToString(price, g_digits),
                            DoubleToString(reference, g_digits),
                            DoubleToString(step, g_digits)));
         return false;
        }
     }

   // (11) cooldown between accepted adds (pairs with the adverse-step gate to
   //      prevent bursty stacking on a fast wick).
   if(InpAvgCooldownSec > 0 && g_lastBasketAvgEntryTime > 0 &&
      (TimeCurrent() - g_lastBasketAvgEntryTime) < InpAvgCooldownSec)
      return false;

   // (12) projected margin gate (pre-trade).
   if(!BasketAveragingMarginOk(isBuy, nextLot, price))
      return false;

   // (13) OrderSend retry loop - mirrors TryOpenTrendRescueEntry. BOTH hard
   //      caps and the margin gate are RE-CHECKED on the refreshed price before
   //      EACH send so a slow/partial fill can never breach the caps. A
   //      DONE_PARTIAL fill returns true (no remainder re-send) so a partial
   //      cannot trigger a second full-lot add.
   bool hadSendFailure = false;
   bool lastSent = false;
   uint lastRetcode = 0;
   string lastSendComment = "";
   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      double freshBid = 0.0, freshAsk = 0.0;
      if(!RefreshCurrentPrices(freshBid, freshAsk))
         return false;
      price = (isBuy ? freshAsk : freshBid);

      // in-loop cap re-check (recomputed live).
      int addsNow = CountAveragingEntries();
      if(InpAvgMaxEntries > 0 && addsNow >= InpAvgMaxEntries)
         return false;
      double totalNow = AveragingTotalLots();
      if(g_normalizedAvgLotCap>0.0 && totalNow+nextLot>g_normalizedAvgLotCap+1.0e-8)
         return false;
      // in-loop margin re-check on the refreshed price.
      if(!BasketAveragingMarginOk(isBuy, nextLot, price))
         return false;

      StrTradeIntent intent;
      InitializeTradeIntent(intent);
      intent.kind=STR_OP_MARKET_DEAL;
      intent.order_type=(isBuy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL);
      intent.requested_volume=nextLot;
      intent.comment=AveragingComment(isBuy);
      intent.new_exposure=true;
      intent.margin_floor_pct=InpAvgMinMarginLevelPct;
      intent.free_margin_reserve=MoneyInput(InpCleanupCostBufferUSD);
      StrActionOutcome outcome=CheckAndSendRequest(intent);
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         // 0.0.40 O1: averaging add opened -> book mutated. MUST be set BEFORE
         // the in-branch CountAveragingEntries()/AveragingTotalLots() reads in
         // the success log so the logged counts reflect the post-open book
         // (byte-identical to 0.0.39's post-OrderSend fresh-scan reads).
         g_bookDirty = true;
         g_lastBasketAvgEntryTime = TimeCurrent();
         double filledPrice = (outcome.result_price > 0.0 ? outcome.result_price : price);
         g_basketAvgLastEntryPrice = filledPrice;
         // 0.0.41 FIX C1: persist the adverse-step price + cooldown time so a
         // recompile/restart mid-episode keeps the gates instead of treating the
         // next add as a first-ever add.
         PersistBasketAvgState();
         // SHARED one-open-per-tick stamp: block the rescue hedge / equity
         // backstop from also opening this tick (they honor g_lastRescueHedgeTime).
         g_lastRescueHedgeTime = TimeCurrent();
         double marginLevelAfter = AccountInfoDouble(ACCOUNT_MARGIN_LEVEL);
         Log(1, StringFormat("Straddle: averaging entry opened: side=%s lots=%.2f price=%s comment=%s order=%I64u deal=%I64u avgEntries=%d totalAvgLots=%.2f maxEntries=%d maxLots=%.2f buriedFloating=%.2f trigger=%.2f step=%s reference=%s marginLevelAfter=%.2f%s",
                             (isBuy ? "BUY" : "SELL"),
                             nextLot,
                             DoubleToString(filledPrice, g_digits),
                             intent.comment,
                             outcome.order,
                             outcome.deal,
                             CountAveragingEntries(),
                             AveragingTotalLots(),
                             InpAvgMaxEntries,
                             g_normalizedAvgLotCap,
                             buriedFloating,
                             trigger,
                             DoubleToString(step, g_digits),
                             DoubleToString(reference, g_digits),
                             marginLevelAfter,
                             (firstAdd ? " firstAdd=yes" : "")));
         return true;
        }

      hadSendFailure = true;
      lastSent = outcome.terminal_result;
      lastRetcode = rc;
      lastSendComment = outcome.result_comment;
      LogOp(StringFormat("Straddle: averaging entry skipped: reason=OrderSend_failed sent=%s attempt=%d side=%s lots=%.2f price=%s retcode=%u result_comment=%s",
                         (outcome.terminal_result ? "true" : "false"), attempt + 1,
                         (isBuy ? "BUY" : "SELL"), nextLot,
                         DoubleToString(price, g_digits),rc,outcome.result_comment));

      switch(ClassifyRetcode(rc))
        {
         case ACT_SUCCESS:
            return false;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:
         case ACT_ABORT_CYCLE:
            return false;
         default:
            LogOp(StringFormat("Straddle: averaging entry failed: side=%s lots=%.2f retcode=%u (%s)",
                               (isBuy ? "BUY" : "SELL"),nextLot,rc,outcome.result_comment));
            return false;
        }
     }

   if(hadSendFailure)
      LogOp(StringFormat("Straddle: averaging entry skipped: reason=OrderSend_failed_retries_exhausted sent=%s retcode=%u comment=%s",
                         (lastSent ? "true" : "false"), lastRetcode, lastSendComment));

   return false;
  }

bool TryOpenTrendRescueEntry(const double bid, const double ask)
  {
   if(!g_trendRescueActive || !InpUseTrendRescueMode)
      return false;
   double coverageGap = TrendRescueCoverageGap();
   if(g_trendRescueDirection != 1 && g_trendRescueDirection != -1)
     {
      LogTrendRescueEntrySkip("missing_direction",
                              "Straddle: trend rescue entry skipped: reason=missing_direction",
                              coverageGap);
      return false;
     }
   if(CountMyPendings() > 0)
     {
      LogOp(StringFormat("Straddle: trend rescue entry skipped: reason=pending_delete_incomplete pendings=%d",
                         CountMyPendings()));
      return false;
     }
   bool isBuy = IsTrendRescueBuy();
   int entries = CountTrendRescueEntries();
   int currentDirectionEntries = CountTrendRescueEntriesForDirection(isBuy);
   bool pressureActive = TrendRescueCoveragePressureActive(coverageGap);
   int currentDirectionEntryCap = InpTrendRescueMaxEntries;
   if(pressureActive && InpTrendRescuePressureMaxEntries > currentDirectionEntryCap)
      currentDirectionEntryCap = InpTrendRescuePressureMaxEntries;
   double equityDD = EquityFloatingDrawdown();
   bool equityCapApplied = false;
   currentDirectionEntryCap = EquityPressureAdjustedEntryCap(currentDirectionEntryCap,
                                                            equityDD,
                                                            equityCapApplied);
   LogEquityPressureStatus(coverageGap, currentDirectionEntries, currentDirectionEntryCap);
   if(equityCapApplied && currentDirectionEntries >= currentDirectionEntryCap)
     {
      string msg = StringFormat("Straddle: trend rescue entry skipped: reason=equity_pressure_backpressure currentDirectionEntries=%d currentDirectionMax=%d totalEntries=%d equityDD=%.2f severeThreshold=%.2f lotMultiplier=%.2f cooldownMultiplier=%.2f stepMultiplier=%.2f coverageGap=%.2f",
                                currentDirectionEntries, currentDirectionEntryCap,
                                entries, equityDD,
                                MoneyInput(InpEquityPressureSevereDDUSD),
                                EquityPressureLotMultiplier(equityDD),
                                EquityPressureCooldownMultiplier(equityDD),
                                EquityPressureStepMultiplier(equityDD),
                                coverageGap);
      LogTrendRescueEntrySkip("equity_pressure_backpressure", msg, coverageGap);
      return false;
     }
   int staleBackpressureCount = 0;
   double staleBackpressureEquityDD = 0.0;
   string staleBackpressureActionability = "";
   bool staleCleanupTriggered = false;
   if(StaleTradeBackpressureDecision(staleBackpressureCount,
                                     staleBackpressureEquityDD,
                                     staleBackpressureActionability,
                                     staleCleanupTriggered))
     {
      if(ShouldPrepareTrendRescueEntrySkipLog("stale_backpressure", coverageGap))
        {
         string msg = StringFormat("Straddle: trend rescue entry skipped: reason=stale_backpressure actionability=%s staleResiduals=%d trigger=%d equityDD=%.2f minEquityDD=%.2f coverageGap=%.2f",
                                   staleBackpressureActionability,
                                   staleBackpressureCount,
                                   InpStaleTradeTriggerCount,
                                   staleBackpressureEquityDD,
                                   MoneyInput(InpStaleTradeMinEquityDDUSD),
                                   coverageGap);
         EmitTrendRescueEntrySkipPrepared("stale_backpressure", msg, coverageGap);
        }
      return false;
     }
   else if(staleCleanupTriggered)
     {
      LogStaleBackpressureBypassedNoActionableCleanup(staleBackpressureCount,
                                                      staleBackpressureEquityDD,
                                                      staleBackpressureActionability,
                                                      coverageGap);
     }
   if(currentDirectionEntryCap <= 0 || currentDirectionEntries >= currentDirectionEntryCap)
     {
      string msg = StringFormat("Straddle: trend rescue entry skipped: reason=max_entries currentDirectionEntries=%d currentDirectionMax=%d totalEntries=%d totalSafetyMax=%d coverageGap=%.2f pressureTarget=%s equityPressure=%s equitySevere=%s",
                                currentDirectionEntries, currentDirectionEntryCap,
                                entries, InpTrendRescueTotalSafetyMaxEntries, coverageGap,
                                (pressureActive ? "true" : "false"),
                                (EquityPressureActive(equityDD) ? "true" : "false"),
                                (EquityPressureSevere(equityDD) ? "true" : "false"));
      LogTrendRescueEntrySkip("max_entries", msg, coverageGap);
      return false;
     }
   if(InpTrendRescueTotalSafetyMaxEntries > 0 && entries >= InpTrendRescueTotalSafetyMaxEntries)
     {
      string msg = StringFormat("Straddle: trend rescue entry skipped: reason=pressure_target_total_safety_cap totalEntries=%d totalSafetyMax=%d currentDirectionEntries=%d currentDirectionMax=%d coverageGap=%.2f pressureTarget=%s",
                                entries, InpTrendRescueTotalSafetyMaxEntries,
                                currentDirectionEntries, currentDirectionEntryCap,
                                coverageGap, (pressureActive ? "true" : "false"));
      LogTrendRescueEntrySkip("pressure_target_total_safety_cap", msg, coverageGap);
      return false;
     }

   int effectiveCooldown = TrendRescueEffectiveCooldownSec(coverageGap);
   if(effectiveCooldown > 0 && g_lastTrendRescueEntryTime > 0 &&
      (TimeCurrent() - g_lastTrendRescueEntryTime) < effectiveCooldown)
     {
      if(ShouldPrepareTrendRescueEntrySkipLog("cooldown", coverageGap))
        {
         int elapsed = (int)(TimeCurrent() - g_lastTrendRescueEntryTime);
         string msg = StringFormat("Straddle: trend rescue entry skipped: reason=cooldown remaining=%d staticCooldown=%d effectiveCooldown=%d coverageGap=%.2f",
                                   effectiveCooldown - elapsed, InpTrendRescueCooldownSec,
                                   effectiveCooldown, coverageGap);
         EmitTrendRescueEntrySkipPrepared("cooldown", msg, coverageGap);
        }
      return false;
     }
   if(bid <= 0.0 || ask <= 0.0)
     {
      string msg = StringFormat("Straddle: trend rescue entry skipped: reason=no_price coverageGap=%.2f",
                                coverageGap);
      LogTrendRescueEntrySkip("no_price", msg, coverageGap);
      return false;
     }

   double price = (isBuy ? ask : bid);

   double effectiveStep = TrendRescueEffectiveStep(coverageGap);
   double stepReference = 0.0;
   int sameDirectionEntries = 0;
   bool firstTrendRescueEntry = !TrendRescueStepReferencePrice(isBuy, stepReference, sameDirectionEntries);
   if(firstTrendRescueEntry && g_trendRescueLastEntryPrice > 0.0)
     {
      double staleLast = g_trendRescueLastEntryPrice;
      g_trendRescueLastEntryPrice = 0.0;
      PersistTrendRescueState();
      LogTrendRescueEntrySkip("first_trend_entry_step_bypass",
          StringFormat("Straddle: trend rescue entry gate reset: reason=first_trend_entry_step_bypass side=%s price=%s staleLast=%s effectiveStep=%.2f coverageGap=%.2f sameDirectionEntries=%d",
                       (isBuy ? "BUY" : "SELL"),
                       DoubleToString(price, g_digits),
                       DoubleToString(staleLast, g_digits),
                       effectiveStep, coverageGap, sameDirectionEntries),
          coverageGap);
     }
    if(!TrendRescueStepReady(isBuy, price, effectiveStep, stepReference, sameDirectionEntries))
      {
       string stepOverrideReason = "";
       if(TrendRescueContinuationOverrideAllowed(isBuy, coverageGap, sameDirectionEntries, bid, ask, stepOverrideReason))
         {
          string overrideMsg = StringFormat("Straddle: trend rescue entry gate bypassed: reason=%s side=%s price=%s reference=%s sameDirectionEntries=%d staticStep=%.2f effectiveStep=%.2f coverageGap=%.2f maxBypassEntries=%d stuckGap=%.2f",
                                            stepOverrideReason,
                                            (isBuy ? "BUY" : "SELL"),
                                            DoubleToString(price, g_digits),
                                            DoubleToString(stepReference, g_digits),
                                            sameDirectionEntries, TrendRescueStep(), effectiveStep, coverageGap,
                                            InpTrendRescuePressureBypassStepMaxEntries,
                                            MoneyInput(InpStuckRecoveryGapUSD));
           if(stepOverrideReason == "coverage_pressure_continuation_override" ||
              stepOverrideReason == "continuation_pressure_override")
              LogTrendRescueEntrySkip(stepOverrideReason, overrideMsg, coverageGap);
           else
              LogTrendRescueEntrySkip(stepOverrideReason, overrideMsg, coverageGap);
         }
       else
         {
          if(ShouldPrepareTrendRescueEntrySkipLog("continuation_step", coverageGap))
             EmitTrendRescueEntrySkipPrepared("continuation_step",
                 StringFormat("Straddle: trend rescue entry skipped: reason=continuation_step side=%s price=%s reference=%s staleLast=%s sameDirectionEntries=%d staticStep=%.2f effectiveStep=%.2f coverageGap=%.2f",
                              (isBuy ? "BUY" : "SELL"),
                              DoubleToString(price, g_digits),
                              DoubleToString(stepReference, g_digits),
                              DoubleToString(g_trendRescueLastEntryPrice, g_digits),
                              sameDirectionEntries, TrendRescueStep(), effectiveStep, coverageGap),
                 coverageGap);
          return false;
         }
      }

   double lots = 0.0;
   double losingSideLots = 0.0, activeTrendLots = 0.0;
   double targetTrendLots = 0.0, missingTrendLots = 0.0;
   if(!TrendRescueLots(currentDirectionEntries, entries, currentDirectionEntryCap,
                       isBuy, lots, losingSideLots, activeTrendLots,
                       targetTrendLots, missingTrendLots, coverageGap))
      return false;

   bool trendRescueImpactProven = false;
   double expectedFunding = 0.0;
   double impactCoverageGap = 0.0;
   double improvementPct = 0.0;
   double expectedMove = 0.0;
   if(!TrendRescueEntryHasEffect(isBuy, lots, currentDirectionEntries, entries,
                                 losingSideLots, activeTrendLots,
                                 trendRescueImpactProven,
                                 expectedFunding, impactCoverageGap,
                                 improvementPct, expectedMove))
      return false;

   if(!TrendRescueOppositeExposureGuardAllows(isBuy, lots,
                                              trendRescueImpactProven,
                                              coverageGap))
      return false;

   LogFmt(2, StringFormat("Straddle: trend rescue impact guard passed: side=%s lots=%.2f expectedMove=%s expectedFunding=%.2f coverageGap=%.2f improvementPct=%.2f impactProven=%s",
                       (isBuy ? "BUY" : "SELL"),
                       lots,
                       DoubleToString(expectedMove, g_digits),
                       expectedFunding,
                       impactCoverageGap,
                       improvementPct,
                       (trendRescueImpactProven ? "true" : "false")));

   if(!TrendRescueMarginOk(isBuy, lots, price, coverageGap))
     {
      string msg = StringFormat("Straddle: trend rescue entry skipped: reason=adaptive_lot_margin side=%s losingSideLots=%.2f activeTrendLots=%.2f targetTrendLots=%.2f missingTrendLots=%.2f nextEntryLot=%.2f coverageGap=%.2f",
                                (isBuy ? "BUY" : "SELL"), losingSideLots, activeTrendLots,
                                targetTrendLots, missingTrendLots, lots, coverageGap);
      LogTrendRescueEntrySkip("adaptive_lot_margin", msg, coverageGap);
      return false;
     }

   bool hadSendFailure = false;
   bool lastSent = false;
   uint lastRetcode = 0;
   string lastSendComment = "";
   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      double freshBid = 0.0, freshAsk = 0.0;
      if(!RefreshCurrentPrices(freshBid, freshAsk))
         return false;
      price = (isBuy ? freshAsk : freshBid);
      stepReference = 0.0;
       sameDirectionEntries = 0;
       if(!TrendRescueStepReady(isBuy, price, effectiveStep, stepReference, sameDirectionEntries))
          {
           string retryOverrideReason = "";
           if(TrendRescueContinuationOverrideAllowed(isBuy, coverageGap, sameDirectionEntries, freshBid, freshAsk, retryOverrideReason))
             {
              string overrideMsg = StringFormat("Straddle: trend rescue entry gate bypassed: reason=%s retry=yes side=%s price=%s reference=%s sameDirectionEntries=%d staticStep=%.2f effectiveStep=%.2f coverageGap=%.2f maxBypassEntries=%d stuckGap=%.2f",
                                                retryOverrideReason,
                                                (isBuy ? "BUY" : "SELL"),
                                                DoubleToString(price, g_digits),
                                                DoubleToString(stepReference, g_digits),
                                                sameDirectionEntries, TrendRescueStep(), effectiveStep, coverageGap,
                                                InpTrendRescuePressureBypassStepMaxEntries,
                                                MoneyInput(InpStuckRecoveryGapUSD));
               if(retryOverrideReason == "coverage_pressure_continuation_override" ||
                  retryOverrideReason == "continuation_pressure_override")
                  LogTrendRescueEntrySkip(retryOverrideReason, overrideMsg, coverageGap);
               else
                  LogTrendRescueEntrySkip(retryOverrideReason, overrideMsg, coverageGap);
             }
           else
             {
              if(ShouldPrepareTrendRescueEntrySkipLog("continuation_step", coverageGap))
                 EmitTrendRescueEntrySkipPrepared("continuation_step",
                     StringFormat("Straddle: trend rescue entry skipped: reason=continuation_step side=%s price=%s reference=%s staleLast=%s sameDirectionEntries=%d staticStep=%.2f effectiveStep=%.2f coverageGap=%.2f",
                                  (isBuy ? "BUY" : "SELL"),
                                  DoubleToString(price, g_digits),
                                  DoubleToString(stepReference, g_digits),
                                  DoubleToString(g_trendRescueLastEntryPrice, g_digits),
                                  sameDirectionEntries, TrendRescueStep(), effectiveStep, coverageGap),
                     coverageGap);
              return false;
             }
          }
       if(!TrendRescueMarginOk(isBuy, lots, price, coverageGap))
          {
           string msg = StringFormat("Straddle: trend rescue entry skipped: reason=adaptive_lot_margin_retry side=%s losingSideLots=%.2f activeTrendLots=%.2f targetTrendLots=%.2f missingTrendLots=%.2f nextEntryLot=%.2f coverageGap=%.2f",
                                     (isBuy ? "BUY" : "SELL"), losingSideLots, activeTrendLots,
                                     targetTrendLots, missingTrendLots, lots, coverageGap);
           LogTrendRescueEntrySkip("adaptive_lot_margin_retry", msg, coverageGap);
           return false;
          }

      StrTradeIntent intent;
      InitializeTradeIntent(intent);
      intent.kind=STR_OP_MARKET_DEAL;
      intent.order_type=(isBuy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL);
      intent.requested_volume=lots;
      intent.comment=TrendRescueComment(isBuy);
      intent.new_exposure=true;
      intent.margin_floor_pct=InpTrendRescueMinMarginLevelPct;
      intent.free_margin_reserve=MoneyInput(InpCleanupCostBufferUSD);
      if(TradeJamActive())
         return false;
      StrActionOutcome outcome=CheckAndSendRequest(intent);
      uint rc=outcome.retcode;
      // 1.1.7: require real order/deal for "success" — retcode0 empty sends were treated as fails
      // after default ACT_FAIL and spammed 16k times on Jan19.
      if(outcome.state==COMPLETED && (outcome.order > 0 || outcome.deal > 0 || outcome.confirmed_volume > 0.0))
        {
         g_bookDirty = true;
         InvalidateTrendRescueSnapshot();
         InvalidateTrendRescueBelowMinLotGuards();
         g_lastTrendRescueEntryTime = TimeCurrent();
         g_trendRescueLastEntryPrice = price;
         PersistTrendRescueState();
         int trendEntriesAfter = CountTrendRescueEntries();
         double marginLevelAfter = AccountInfoDouble(ACCOUNT_MARGIN_LEVEL);
         double filledPrice = (outcome.result_price > 0.0 ? outcome.result_price : price);
         Log(1, StringFormat("Straddle: trend rescue entry opened: side=%s lots=%.2f price=%s comment=%s order=%I64u deal=%I64u ticket=%I64u trendEntries=%d currentDirectionEntries=%d currentDirectionMax=%d totalSafetyMax=%d marginLevelAfter=%.2f losingSideLots=%.2f activeTrendLots=%.2f targetTrendLots=%.2f missingTrendLots=%.2f coverageGap=%.2f effectiveCooldown=%d effectiveStep=%.2f stepReference=%s sameDirectionEntries=%d",
                             (isBuy ? "BUY" : "SELL"),
                             lots,
                             DoubleToString(filledPrice, g_digits),
                             intent.comment,
                             outcome.order,
                             outcome.deal,
                             (outcome.order > 0 ? outcome.order : outcome.deal),
                             trendEntriesAfter,
                             CountTrendRescueEntriesForDirection(isBuy),
                             currentDirectionEntryCap,
                             InpTrendRescueTotalSafetyMaxEntries,
                             marginLevelAfter,
                             losingSideLots, activeTrendLots, targetTrendLots,
                             missingTrendLots, coverageGap, effectiveCooldown,
                             effectiveStep, DoubleToString(stepReference, g_digits),
                             sameDirectionEntries));
         return true;
        }

      hadSendFailure = true;
      lastSent = outcome.terminal_result;
      lastRetcode = rc;
      lastSendComment = outcome.result_comment;
      if(rc == 0 || (outcome.order == 0 && outcome.deal == 0))
         NoteTradeJam(0, "trend-rescue-entry");

      // 1.1.7: throttle fail logs (was every tick → multi-GB + tester stall)
      LogState(1, "tr-entry-fail",
               StringFormat("Straddle: ERR TR-entry fail side=%s lots=%.2f rc=%u (backoff)",
                            (isBuy ? "BUY" : "SELL"), lots, rc),
               30);

      switch(ClassifyRetcode(rc))
        {
         case ACT_SUCCESS:
            // empty "success" without order/deal
            NoteTradeJam(0, "trend-rescue-empty-success");
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:
         case ACT_ABORT_CYCLE:
            return false;
         default:
            return false;
        }
      }

   if(hadSendFailure)
      LogOp(StringFormat("Straddle: trend rescue entry skipped: reason=OrderSend_failed_retries_exhausted sent=%s retcode=%u comment=%s",
                         (lastSent ? "true" : "false"), lastRetcode, lastSendComment));

   return false;
  }

bool CloseTrendRescueEntryIfProfitable(const ulong ticket,
                                       const double effectiveHarvestTarget,
                                       const double coverageGap)
  {
   if(effectiveHarvestTarget <= 0.0)
      return false;
   if(!PositionSelectByTicket(ticket))
      return false;
   if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
      return false;
   if(PositionGetString(POSITION_SYMBOL) != g_sym)
      return false;
   if(!IsTrendRescueComment(PositionGetString(POSITION_COMMENT)))
      return false;

   double profitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   if(profitAndSwap < effectiveHarvestTarget)
      return false;

   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      if(!PositionSelectByTicket(ticket))
         return true;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic ||
         PositionGetString(POSITION_SYMBOL) != g_sym ||
         !IsTrendRescueComment(PositionGetString(POSITION_COMMENT)))
         return false;

      profitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(profitAndSwap < effectiveHarvestTarget)
         return false;

      double closeVolume=PositionGetDouble(POSITION_VOLUME);
      StrActionOutcome outcome=ExecutePositionReduction(ticket,closeVolume,"STR TR CLOSE");
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         g_bookDirty = true;   // 0.0.40 O1: trend-rescue entry closed (full/partial) -> book mutated
         InvalidateTrendRescueSnapshot();
         InvalidateTrendRescueBelowMinLotGuards();
         if(!PositionSelectByTicket(ticket))
           {
            Log(1, StringFormat("Straddle: trend rescue entry harvested ticket #%I64u profitAndSwap %.2f >= %.2f coverageGap=%.2f rawTarget=%.2f effectiveStaticTarget=%.2f",
                                ticket, profitAndSwap, effectiveHarvestTarget,
                                coverageGap, InpTrendRescueProfitTargetUSD,
                                MoneyInput(InpTrendRescueProfitTargetUSD)));
            return true;
           }
         continue;
        }

      switch(ClassifyRetcode(rc))
        {
         case ACT_SUCCESS:
            return false;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:
         case ACT_ABORT_CYCLE:
            return false;
         default:
            LogOp(StringFormat("Straddle: trend rescue entry harvest close of position #%I64u failed, retcode %u (%s)",
                               ticket,rc,outcome.result_comment));
            return false;
        }
     }

   return !PositionSelectByTicket(ticket);
  }

bool TryHarvestTrendRescueEntries()
  {
   if(!g_trendRescueActive || MoneyInput(InpTrendRescueProfitTargetUSD) <= 0.0)
      return false;

   double coverageGap = TrendRescueCoverageGap();
   double effectiveHarvestTarget = TrendRescueEffectiveHarvestTarget(coverageGap);
   if(effectiveHarvestTarget <= 0.0)
      return false;

   LogFmt(2, StringFormat("Straddle: trend rescue harvest target: coverageGap=%.2f effectiveHarvestTarget=%.2f rawTarget=%.2f effectiveStaticTarget=%.2f share=%.2f",
                       coverageGap, effectiveHarvestTarget,
                       InpTrendRescueProfitTargetUSD, MoneyInput(InpTrendRescueProfitTargetUSD),
                       InpTrendRescueHarvestGapShare));

   bool closedAny = false;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(!IsTrendRescueComment(PositionGetString(POSITION_COMMENT)))
         continue;
      if(CloseTrendRescueEntryIfProfitable(ticket, effectiveHarvestTarget, coverageGap))
         closedAny = true;
     }
   return closedAny;
  }

void EnterTrendRescue(const int direction,
                      const double mid,
                      const double gridMin,
                      const double gridMax,
                      const double cycleNet,
                      const int positions,
                      const int pendings)
  {
   if(direction != 1 && direction != -1)
      return;

   bool wasTrendRescue = g_trendRescueActive;
   g_trendRescueActive = true;
   g_trendRescueDirection = direction;
   if(!wasTrendRescue)
      g_lastTrendRescueDirectionSwitchTime = 0;
   g_rescueHolding = false;
   g_tearingDown = false;
   g_nextTearTry = 0;
   g_nextBuildTry = 0;
   g_rescueHedgeModeLogged = false;
   g_rescueCloseByNoticeLogged = false;
   GlobalVariableDel(RHVar());
   GlobalVariableDel(TDVar());
   ClearPersistedTearDownThreshold();
   ClearPersistedBasketTearDownTag();   // 0.0.38: entering trend rescue is not a basket teardown
   ClearRescueHedgeTime();

   if(!wasTrendRescue || !g_rescueAnchorTrusted || g_rescueAnchorBalance <= 0.0)
     {
      double cycleRealizedCredit = MathMax(0.0, CycleRealized());
      double balance = AccountInfoDouble(ACCOUNT_BALANCE);
      g_rescueAnchorBalance = balance - cycleRealizedCredit;
      if(g_rescueAnchorBalance <= 0.0)
         g_rescueAnchorBalance = balance;
      g_rescueAnchorTrusted = (g_rescueAnchorBalance > 0.0);
      PersistRescueAnchorBalance();
     }

   PersistTrendRescueState();
   Log(1, StringFormat("Straddle: trend rescue start: direction=%s, oldGrid=%s..%s, cycleNet=%.2f, positions=%d, pendings=%d, mid=%s",
                       TrendRescueDirectionName(),
                       DoubleToString(gridMin, g_digits),
                       DoubleToString(gridMax, g_digits),
                       cycleNet, positions, pendings,
                       DoubleToString(mid, g_digits)));
   LogTrendRescueStatus("entry", true);

   int rescuePendingsBefore = CountMyPendings();
   if(rescuePendingsBefore > 0)
      LogTrendRescueStatus("before pending cleanup", true);
   DeleteRescuePendings();
   InvalidateTrendRescueSnapshot();
   if(rescuePendingsBefore > 0)
      LogTrendRescueStatus("after pending cleanup", true);
  }

void ProcessTrendRescue(const double bid, const double ask)
  {
   if(!g_trendRescueActive)
      return;

   int positions = CountMyPositions();
   int pendings = CountMyPendings();
   LogTrendRescueStatus("periodic", false);

   if(positions == 0)
     {
      if(pendings > 0)
        {
         bool pendingActionReady = (g_nextRescueTry == 0 || TimeCurrent() >= g_nextRescueTry);
         if(pendingActionReady)
            LogTrendRescueStatus("before pending cleanup", true);
         int pendingsBeforeDelete = pendings;
         DeleteRescuePendings();
         int pendingsAfterDelete = CountMyPendings();
         if(pendingsAfterDelete != pendingsBeforeDelete)
            InvalidateTrendRescueSnapshot();
         if(pendingActionReady)
            LogTrendRescueStatus("after pending cleanup", true);
         return;
        }

      ClearTrendRescueState(true);
      g_pauseUntil = TimeCurrent() + InpPauseSeconds;
      ResetCycleState();
      Log(1, StringFormat("Straddle: trend rescue flat, rebuilding after %d s pause; next normal cycle starts with fresh market anchor", InpPauseSeconds));
      return;
     }

   if(TrendRescueProfitCoveredReset())
      return;

   {
      bool deleteComplete = (pendings <= 0);
      int pendingsAfterDelete = pendings;
      if(pendings > 0)
        {
         bool pendingActionReady = (g_nextRescueTry == 0 || TimeCurrent() >= g_nextRescueTry);
         if(pendingActionReady)
            LogTrendRescueStatus("before pending cleanup", true);
         int pendingsBeforeDelete = pendings;
         deleteComplete = DeleteRescuePendings();
         pendingsAfterDelete = CountMyPendings();
         if(pendingsAfterDelete != pendingsBeforeDelete)
            InvalidateTrendRescueSnapshot();
         if(pendingActionReady)
            LogTrendRescueStatus("after pending cleanup", true);
        }

      if(bid > 0.0 && ask > 0.0)
        {
           UpdateLegTracking(bid, ask);
           ManagePerLegTrailing(bid, ask);
           TryHarvestRescueHedges(); // 0.0.34: harvest before open, mirror rescue-hold ordering
           bool normalCleanupProgress = false;
           if(TryTrendRescuePairFundedCleanup("trend-rescue-pair"))
              normalCleanupProgress = true;
            if(OrphanedPairProfitReservePending("trend-rescue-pair-post"))
              {
               LogTrendRescueCleanupDiag("pair", "orphaned_reserve_tick_guard",
                                         StringFormat("orphanedPairProfitReserve=%.2f reserveBudget=%.2f",
                                                      g_deferredPairProfitReserve,
                                                      TrendRescuePairOrphanedReserveBudget("trend-rescue-pair-post")));
              }
            if(TryTrendRescueFloatingPairCleanup("trend-rescue-floating-pair"))
               normalCleanupProgress = true;
            if(TrendRescueProfitCoveredReset())
               return;
           TryHarvestTrendRescueEntries();
           if(TrendRescueProfitCoveredReset())
              return;
           if(TryProtectedProfitCleanup("trend-rescue-protected"))
              normalCleanupProgress = true;
           if(TrendRescueProfitCoveredReset())
              return;
           if(TryTrendRescueProfitFundedLoserCleanup("trend-rescue"))
              normalCleanupProgress = true;
           if(TrendRescueProfitCoveredReset())
              return;
           if(TryTrendRescueEntryProfitFundedCleanup("trend-rescue-entry"))
              normalCleanupProgress = true;
           if(TrendRescueProfitCoveredReset())
              return;
           double stuckCoverageGap = TrendRescueCoverageGap();
           if(TryTrendRescueStuckRecoveryCleanup("trend-rescue-stuck", stuckCoverageGap, normalCleanupProgress))
              normalCleanupProgress = true;
           if(TrendRescueProfitCoveredReset())
              return;
           if(TryStaleTradeCleanup("trend-rescue-stale"))
              normalCleanupProgress = true;
           if(TrendRescueProfitCoveredReset())
              return;
           UpdateRollingTrendRescueDirection(bid, ask);
            if(pendingsAfterDelete == 0)
              {
               if(CanTrade())
                  TryOpenTrendRescueEntry(bid, ask);
              }
            else
              LogOp(StringFormat("Straddle: trend rescue entry skipped: reason=pending_delete_incomplete pendings=%d delete_ok=%s",
                                 pendingsAfterDelete, (deleteComplete ? "true" : "false")));
           // 0.0.34: open a net-sized rescue hedge in trend rescue too; self-gates
           // on InpUseRescueHedge/cooldown/max-hedges/loss-trigger/margin.
           // 0.0.36: UNGATED from pending-order state. In trend rescue the EA
           // keeps continuation pendings, so pendingsAfterDelete>0 nearly every
           // tick and the old 'if(pendingsAfterDelete == 0)' gate skipped this
           // open forever -> STR RHG never fired and orphan-loser equity DD rode
           // unbounded. The rescue hedge is an independent net-exposure position
           // sized from NetExposureLots and self-gates internally on
           // InpUseRescueHedge/IsHedgingAccount/cooldown/max-hedges/loss-trigger/
           // margin; it must NOT depend on pending-order state. The trend-rescue
           // ENTRY above keeps its pendings gate.
            if(CanTrade())
               TryOpenRescueHedge(bid, ask);
         }
     }
  }

void LogRescueCloseByNoticeOnce()
  {
   if(g_rescueCloseByNoticeLogged)
      return;
   g_rescueCloseByNoticeLogged = true;
   Log(1, "Straddle: rescue close-by support is not implemented in v0.0.14; using realized-bank chunk cleanup and guarded rescue hedge only");
  }

//+------------------------------------------------------------------+
//| 1.0.5: true when a pending order sits inside the broker freeze/   |
//| spread band of market, where a cancel/modify would be rejected    |
//| ("...close to market", rc 10016) and the tester logs it as an     |
//| error. Callers DEFER the delete instead of issuing a doomed one.  |
//+------------------------------------------------------------------+
bool IsPendingWithinFreeze(const ulong ticket)
  {
   if(!OrderSelect(ticket))
      return false;
   double price = OrderGetDouble(ORDER_PRICE_OPEN);
   if(price <= 0.0)
      return false;
   ENUM_ORDER_TYPE type = (ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);

   long   freeze = SymbolInfoInteger(g_sym, SYMBOL_TRADE_FREEZE_LEVEL);
   long   stops  = SymbolInfoInteger(g_sym, SYMBOL_TRADE_STOPS_LEVEL);
   long   lvl    = (freeze > stops ? freeze : stops);
   double point  = SymbolInfoDouble(g_sym, SYMBOL_POINT);

   double bid = 0.0, ask = 0.0;
   if(!RefreshCurrentPrices(bid, ask))
      return false;                       // no fresh price -> let the normal path run
   double spread = (ask > bid && bid > 0.0) ? (ask - bid) : 0.0;
   double guard  = (double)lvl * point + spread + TickSize();
   if(guard <= 0.0)
      return false;

   switch(type)
     {
      case ORDER_TYPE_BUY_STOP:
      case ORDER_TYPE_BUY_LIMIT:
      case ORDER_TYPE_BUY_STOP_LIMIT:
         return (ask > 0.0 && MathAbs(price - ask) <= guard);
      case ORDER_TYPE_SELL_STOP:
      case ORDER_TYPE_SELL_LIMIT:
      case ORDER_TYPE_SELL_STOP_LIMIT:
         return (bid > 0.0 && MathAbs(price - bid) <= guard);
      default:
         return false;
     }
  }

//+------------------------------------------------------------------+
//| Delete one pending order with bounded retry.                      |
//| ACT_SKIP_WAIT / ACT_ABORT_CYCLE return SILENTLY (see              |
//| CloseOnePosition) - the teardown back-off owns the retry.         |
//| Deterministic ACT_FAIL codes log through the LogOp throttle.      |
//+------------------------------------------------------------------+
bool DeleteOnePending(const ulong ticket)
  {
   for(int attempt=0; attempt<=g_validatedMaxRetries; attempt++)
     {
      if(!OrderSelect(ticket))
         return true;                                   // already gone (filled or deleted)
      if(IsPendingWithinFreeze(ticket))
         return false;                                  // 1.0.5: inside freeze band -> defer; do NOT issue (avoids tester "close to market" error)
      StrActionOutcome outcome=ExecutePendingDelete(ticket,"STR DELETE");
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         g_bookDirty=true;
         return !OrderSelect(ticket);
        }
      if(outcome.state==PENDING_RECONCILIATION || outcome.state==PARTIAL)
         return false;
      switch(ClassifyRetcode(rc))
        {
         case ACT_SUCCESS:
            return false;
         case ACT_RETRY_REFRESH:
            break;
         case ACT_BACKOFF:
            Sleep(RetryBackoffDelayMs(attempt));
            break;
         case ACT_SKIP_WAIT:    // market closed / trading disabled - expected, retry later, NO log
         case ACT_ABORT_CYCLE:  // hard limit - stop now, NO per-ticket log
            return false;
         default:                                       // genuinely unexpected codes only
            LogOp(StringFormat("Straddle: delete of order #%I64u failed, retcode %u (%s)",
                               ticket,rc,outcome.result_comment));
            return false;
        }
     }
   return !OrderSelect(ticket);
  }

bool DeleteGridPendingSlot(const bool isBuy, const int level)
  {
   const string expectedComment = LegComment(isBuy, level);
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      const ulong ticket = OrderGetTicket(i);
      if(ticket == 0 ||
         OrderGetInteger(ORDER_MAGIC) != InpMagic ||
         OrderGetString(ORDER_SYMBOL) != g_sym ||
         OrderGetString(ORDER_COMMENT) != expectedComment)
         continue;
      return DeleteOnePending(ticket);
     }
   return true;
  }

//+------------------------------------------------------------------+
//| Market-close every position of this EA. True if flat afterwards.  |
//| ONLY called from the teardown path (full close). Per-leg trailing |
//| exits are handled by broker-side SLs, not EA market closes.       |
//| 0.0.41 FIX B: optional basket no-loss mid-loop guard. When         |
//| basketGuard && basketThreshold>0, the whole-book floating P/L is    |
//| re-read FRESH BEFORE each ticket close; if it has dropped below     |
//| +basketThreshold the loop ABORTS and returns false, leaving the     |
//| remaining (now red) legs OPEN so they are NEVER banked below the    |
//| basket take-profit promise. ProcessTearDown leaves g_tearingDown    |
//| set, so the close re-arms and re-checks next tick when green again. |
//| Default params (basketGuard=false) keep every non-basket caller     |
//| bit-for-bit 0.0.40. The fresh re-read re-walks the book only after  |
//| a SUCCESSFUL close (CloseOnePosition sets g_bookDirty); on a close   |
//| FAILURE the cached floating is reused, which is safe because a       |
//| failed close banks nothing. In the tester (frozen single tick) the  |
//| floating only improves as red legs vanish, so the guard never trips.|
//+------------------------------------------------------------------+
bool CloseAllPositions(const bool basketGuard = false, const double basketThreshold = 0.0)
  {
   // Non-basket cycle teardown excludes registered Float tickets. The account-
   // level basket guard remains whole-book because its positive threshold was
   // evaluated across the same whole-book surface.
   // 1.1.22: daily stop-and-close must flatten EVERYTHING including floats.
   bool excludeFloat=(InpUseFloatReanchor && !basketGuard && !g_dailyStopForceClose);
   // collect tickets first - indices shift while closing
   ulong tickets[];
   int   n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(excludeFloat && IsFloatTicket(ticket))
         continue;
      ArrayResize(tickets, n + 1);
      tickets[n++] = ticket;
     }
   bool all = true;
   for(int i = 0; i < n; i++)
     {
      if(basketGuard && basketThreshold > 0.0)
        {
         // FRESH whole-book floating: g_bookDirty was set by each prior
         // CloseOnePosition (4284), so this re-walks with live prices after a
         // banked leg; on the first iteration it reflects the pre-loop book.
         double guardLots = 0.0;
         double guardFloating = OpenFloatingPL(guardLots);
         if(guardFloating < basketThreshold)
           {
            Log(2, StringFormat("Straddle: basket close-all ABORTED mid-loop - floating %.2f dropped below +%.2f; keeping remaining legs open (no-loss guard), re-arming next tick",
                                guardFloating, basketThreshold));
            return false;   // g_tearingDown stays set; ProcessTearDown re-arms / re-checks next tick
           }
        }
      if(!CloseOnePosition(tickets[i]))
         all = false;
     }
   return all;
  }

//+------------------------------------------------------------------+
//| Delete every pending order of this EA. True if none remain.       |
//| Called from teardown and rescue hold; active-cycle stale cleanup  |
//| uses CleanupOffGridPendings() and never closes positions.         |
//+------------------------------------------------------------------+
bool DeleteAllPendings()
  {
   ulong tickets[];
   int   n = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;
      ArrayResize(tickets, n + 1);
      tickets[n++] = ticket;
     }
   bool all = true;
   for(int i = 0; i < n; i++)
      if(!DeleteOnePending(tickets[i]))
         all = false;
   return all;
  }

//+------------------------------------------------------------------+
//| Rescue-mode pending deletion with bounded retry/back-off.         |
//| It never closes positions; it only removes future grid exposure.  |
//+------------------------------------------------------------------+
bool DeleteRescuePendings()
  {
   // 1.1.9: keep buy/sell stops on the fixed grid chart
   if(InpKeepGridPendings)
      return true;

   if(g_nextRescueTry > 0 && TimeCurrent() < g_nextRescueTry)
      return false;
   g_nextRescueTry = 0;

   int before = CountMyPendings();
   if(before <= 0)
      return true;

   DeleteAllPendings();
   int after = CountMyPendings();
   if(after > 0 && after >= before)
      g_nextRescueTry = TimeCurrent() + InpRetrySeconds;
   return (after == 0);
  }

void ClearRescueHold()
  {
   g_rescueHolding = false;
   g_nextRescueTry = 0;
   g_lastRescueStatusLog = 0;
   g_rescueHedgeModeLogged = false;
   g_rescueCloseByNoticeLogged = false;
   GlobalVariableDel(RHVar());
   ClearRescueAnchorBalance();
   ClearRescueHedgeTime();
   ClearTrendRescueState(false);
  }


//+------------------------------------------------------------------+
//| 0.0.44: FLOAT RE-ANCHOR. A NO-CLOSE sibling of legacy close-reopen path.   |
//| ===========================================================       |
//| THE NO-REALIZE INVARIANT (#1 requirement): this function issues   |
//| ZERO OrderSend. It does NOT close a single position and does NOT  |
//| create a single deal. Balance literally cannot move here. It only |
//| (1) deletes pendings (no realized P/L), (2) registers every live  |
//| non-orphan/non-rescue-hedge/non-trend-rescue cycle leg into       |
//| g_floatTickets[] (keeping its STR B#/S# comment), (3) clears the  |
//| jammed teardown/rescue/trend state and ResetCycleState()s so a    |
//| FRESH cycle anchors at the CURRENT price next tick. Floated legs  |
//| then claim NO grid slot (occupancy is ticket-based) and are       |
//| excluded from CycleNet/CycleRealized; they are closed ONLY when   |
//| GREEN (whole-book basket-TP or per-ticket FloatGreenClose).       |
//| Hard no-op when InpUseFloatReanchor=false.                        |
//+------------------------------------------------------------------+
bool FloatReanchor(const string reason)
  {
   if(!InpUseFloatReanchor)
      return false;

   EnsureBookAggregates();

   // STEP 1: 1.1.9 KeepGridPendings — leave buy/sell stops on the fixed chart
   // levels. Only wipe pendings when keep is off (legacy recovery behavior).
   const double keepAnchor = g_anchor;
   const double keepStep   = g_cycleGridStepDistance;
   if(!InpKeepGridPendings)
      DeleteAllPendings();

   // STEP 2: collect eligible cycle legs (not already float / RHG / TR).
   // 1.1.4: sort by worst floating P/L first so a positive MaxFloatLots cap
   // floats the most toxic legs first (PARTIAL float) instead of all-or-nothing REFUSE.
   ulong  eligTicket[];
   double eligVol[];
   double eligFloat[];
   int    eligN = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      string comment = PositionGetString(POSITION_COMMENT);
      if(IsFloatTicket(ticket))
         continue;
      if(IsRescueHedgeComment(comment))
         continue;                       // STR RHG backstop hedge - never float
      if(IsTrendRescueComment(comment))
         continue;                       // STR TR recovery engine leg - never float
      double volume = PositionGetDouble(POSITION_VOLUME);
      if(volume <= 0.0)
         continue;
      ArrayResize(eligTicket, eligN + 1);
      ArrayResize(eligVol, eligN + 1);
      ArrayResize(eligFloat, eligN + 1);
      eligTicket[eligN] = ticket;
      eligVol[eligN] = volume;
      eligFloat[eligN] = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP)
                         - EstimatedOpenPositionCommission(ticket, volume);
      eligN++;
     }

   if(eligN <= 0)
     {
      Log(1, StringFormat("Straddle: float re-anchor found NO convertible cycle legs (reason: %s)", reason));
      return false;
     }

   // Worst floating first (most negative USD).
   for(int a = 0; a < eligN - 1; a++)
     {
      int best = a;
      for(int b = a + 1; b < eligN; b++)
        {
         if(eligFloat[b] < eligFloat[best])
            best = b;
        }
      if(best != a)
        {
         ulong t = eligTicket[a]; eligTicket[a] = eligTicket[best]; eligTicket[best] = t;
         double v = eligVol[a]; eligVol[a] = eligVol[best]; eligVol[best] = v;
         double f = eligFloat[a]; eligFloat[a] = eligFloat[best]; eligFloat[best] = f;
        }
     }

   const double volStep = MathMax(1.0e-8, SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP));
   const double room = (g_normalizedFloatLotCap > 0.0
                        ? MathMax(0.0, g_normalizedFloatLotCap - CurrentFloatLots())
                        : 1.0e100); // uncapped

   int    registered = 0;
   double registeredLots = 0.0;
   double used = 0.0;
   int    skippedCap = 0;
   for(int k = 0; k < eligN; k++)
     {
      if(g_normalizedFloatLotCap > 0.0 &&
         used + eligVol[k] > room + volStep * 0.5)
        {
         skippedCap++;
         continue;
        }
      AddFloatTicket(eligTicket[k]);
      registered++;
      registeredLots += eligVol[k];
      used += eligVol[k];
     }

   if(registered <= 0)
     {
      // Cap full with no room for even the smallest worst leg.
      LogState(1, "float-refuse",
               StringFormat("Straddle: float re-anchor NO ROOM under cap=%.2f currentFloat=%.2f elig=%d reason=%s",
                            g_normalizedFloatLotCap, CurrentFloatLots(), eligN, reason),
               60);
      return false;
     }

   if(skippedCap > 0)
      Log(1, StringFormat("Straddle: float re-anchor PARTIAL - floated %d legs (%.2f lots), skipped %d over cap %.2f (reason: %s)",
                          registered, registeredLots, skippedCap, g_normalizedFloatLotCap, reason));

   // PersistFloatRegistry() already ran inside each AddFloatTicket. Clear only
   // the legacy cycle state owned by this completed no-close transition.
   ClearRescueHold();                    // also clears trend-rescue (idempotent)
   g_tearingDown   = false;
   g_nextTearTry   = 0;
   ClearPersistedTearDownThreshold();
   ClearPersistedBasketTearDownTag();
   // Clear teardown marker GV so restart cannot resume teardown against fresh cycle.
   GlobalVariableDel(TDVar());
   if(InpUseBasketAveraging)
      ClearBasketAvgState();
   ResetCycleState();
   // 1.1.9: restore same grid geometry so existing pendings stay valid on chart
   if(InpKeepGridPendings && keepAnchor > 0.0)
     {
      g_anchor = keepAnchor;
      if(keepStep > 0.0)
        {
         g_cycleGridStepDistance = keepStep;
         PersistCycleGridStepDistance();
        }
      g_cycleStart = TimeCurrent();
      g_cycleStartTrusted = true;
      PersistCycleStart();
      LogAlways(StringFormat("Straddle: FloatReanchor - %d legs FLOATED (%.2f lots); KEEP grid pendings anchor=%s (reason: %s)",
                             registered, registeredLots, DoubleToString(g_anchor, g_digits), reason));
     }
   else
      LogAlways(StringFormat("Straddle: FloatReanchor - %d legs FLOATED (%.2f lots, NO close, NO realized loss); fresh cycle will anchor at current price next tick (reason: %s)",
                             registered, registeredLots, reason));
   g_bookDirty = true;
   return true;
  }

//+------------------------------------------------------------------+
//| 0.0.44: close ONE floated ticket - GREEN ONLY. The hard guarantee |
//| that a float close is always green: closes ONLY IF the leg's      |
//| floating (profit+swap minus effective commission) >= the buffer   |
//| (>=0 => green). Captures POSITION_ID into g_floatClosedPositionIds|
//| BEFORE the close (so CycleRealized excludes the green) and prunes  |
//| the ticket from the registry on success. NEVER closes a red leg.  |
//+------------------------------------------------------------------+
bool CloseOneFloatTicket(const ulong ticket)
  {
   if(TradeJamActive())
      return false;
   if(!PositionSelectByTicket(ticket))
     {
      RemoveFloatTicket(ticket);         // position gone - self-heal the registry
      return false;
     }
   double volume      = PositionGetDouble(POSITION_VOLUME);
   double legFloating = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP)
                        - CommissionPerLotEffective() * volume;
   if(legFloating < MoneyInput(InpFloatCloseBufferUSD))
      return false;                      // not green enough - HOLD (no-realize invariant)

   ulong pid = (ulong)PositionGetInteger(POSITION_IDENTIFIER);
   // Record BEFORE close so the green never folds into the fresh cycle's realized.
   AddFloatClosedPositionId(pid);

   // 1.1.7: multi-attempt close; retcode 0 used to leave +$68 greens stuck (Jan19).
   for(int attempt = 0; attempt <= g_validatedMaxRetries + 1; attempt++)
     {
      if(!PositionSelectByTicket(ticket))
        {
         RemoveFloatTicket(ticket);
         g_bookDirty = true;
         LogAlways(StringFormat("Straddle: float green-close - banked floated leg #%I64u (%.2f lots, +%.2f); registry now %d tickets",
                                ticket, volume, legFloating, g_floatCount));
         return true;
        }
      // re-check still green after retries / price change
      double liveFloat = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP)
                         - CommissionPerLotEffective() * PositionGetDouble(POSITION_VOLUME);
      if(liveFloat < MoneyInput(InpFloatCloseBufferUSD))
         return false;
      legFloating = liveFloat;

      if(CloseOnePosition(ticket))
        {
         RemoveFloatTicket(ticket);
         g_bookDirty = true;
         LogAlways(StringFormat("Straddle: float green-close - banked floated leg #%I64u (%.2f lots, +%.2f); registry now %d tickets",
                                ticket, volume, legFloating, g_floatCount));
         return true;
        }
      double b=0.0, a=0.0;
      RefreshCurrentPrices(b, a);
      Sleep(RetryBackoffDelayMs(attempt));
     }
   LogState(1, "float-green-fail",
            StringFormat("Straddle: ERR float-green-close FAIL ticket=#%I64u floating=%.2f (will retry)",
                         ticket, legFloating),
            30);
   return false;
  }

//+------------------------------------------------------------------+
//| 0.0.44: per-ticket green-only float close pass. Iterates the float |
//| registry and closes EVERY leg whose floating is green (>= buffer). |
//| Snapshots tickets first because closing prunes the registry. Hard  |
//| no-op when the registry is empty (default-off).                    |
//+------------------------------------------------------------------+
bool FloatGreenClose()
  {
   if(g_floatCount <= 0)
      return false;
   ulong snapshot[];
   int   m = g_floatCount;
   ArrayResize(snapshot, m);
   for(int i = 0; i < m; i++)
      snapshot[i] = g_floatTickets[i];

   bool any = false;
   for(int i = 0; i < m; i++)
     {
      if(!PositionSelectByTicket(snapshot[i]))
        {
         RemoveFloatTicket(snapshot[i]); // gone - self-heal
         continue;
        }
      if(CloseOneFloatTicket(snapshot[i]))
         any = true;
     }
   return any;
  }


//+------------------------------------------------------------------+
//| Enter rescue hold: persist hold state, clear teardown intent,     |
//| delete pendings only, and leave positions open.                   |
//+------------------------------------------------------------------+
void EnterRescueHold(const string reason)
  {
   Log(1, "Straddle: " + reason);
   ClearTrendRescueState(false);
   bool wasRescueHolding = g_rescueHolding;
   g_rescueHolding = true;
   g_tearingDown   = false;
   g_nextTearTry   = 0;
   g_nextBuildTry  = 0;
   GlobalVariableDel(TDVar());
   ClearPersistedTearDownThreshold();
   ClearPersistedBasketTearDownTag();   // 0.0.38: a basket teardown that diverts to rescue hold is no longer a basket teardown
   if(!wasRescueHolding && (!g_rescueAnchorTrusted || g_rescueAnchorBalance <= 0.0))
     {
      g_rescueAnchorBalance = AccountInfoDouble(ACCOUNT_BALANCE);
      g_rescueAnchorTrusted = (g_rescueAnchorBalance > 0.0);
      PersistRescueAnchorBalance();
     }
   if(!GlobalVariableSet(RHVar(), (double)TimeCurrent()))
      Log(0, "Straddle: WARN could not set rescue hold marker");
   LogRescueStatus("entry", true);
   LogRescueCloseByNoticeOnce();
   int rescuePendingsBefore = CountMyPendings();
   if(rescuePendingsBefore > 0)
      LogRescueStatus("before pending cleanup", true);
   DeleteRescuePendings();
   if(rescuePendingsBefore > 0)
      LogRescueStatus("after pending cleanup", true);
  }

bool EnsureTrustedCycleStartForClose(const string reason, const int positions)
  {
   if(positions <= 0)
      return true;
   if(g_cycleStartTrusted && g_cycleStart > 0)
      return true;

   EnterRescueHold(StringFormat("%s - cycle start marker missing/untrusted after restart, positions %d, pendings %d, deleting pendings only, holding positions; balance protected but equity risk remains",
                                reason, positions, CountMyPendings()));
   return false;
  }

//+------------------------------------------------------------------+
//| Rescue state machine. Blocks refill/start, continues profitable   |
//| broker-side trailing and realized-funded losing cleanup, and      |
//| exits only once flat or safely positive enough for teardown.      |
//+------------------------------------------------------------------+
void ProcessRescueHold(const double bid, const double ask)
  {
   int positions = CountMyPositions();
   int pendings  = CountMyPendings();
   LogRescueStatus("periodic", false);
   LogRescueCloseByNoticeOnce();

   if(positions == 0)
     {
      if(pendings > 0)
        {
         bool pendingActionReady = (g_nextRescueTry == 0 || TimeCurrent() >= g_nextRescueTry);
         if(pendingActionReady)
            LogRescueStatus("before pending cleanup", true);
         DeleteRescuePendings();
         if(pendingActionReady)
            LogRescueStatus("after pending cleanup", true);
         return;
        }

      ClearRescueHold();
      g_pauseUntil = TimeCurrent() + InpPauseSeconds;
      ResetCycleState();
      // 0.0.41 FIX C1: clear the averaging gate GVs on rescue-hold flat so a new
      // episode never inherits a stale adverse-step reference. Gated on
      // InpUseBasketAveraging (default-OFF run touches no GlobalVariable here).
      if(InpUseBasketAveraging)
         ClearBasketAvgState();
      Log(1, StringFormat("Straddle: rescue hold flat, rebuilding after %d s pause; next normal cycle starts with fresh market anchor", InpPauseSeconds));
      return;
     }

    {
      bool pendingActionReady = (pendings > 0 && (g_nextRescueTry == 0 || TimeCurrent() >= g_nextRescueTry));
      if(pendingActionReady)
         LogRescueStatus("before pending cleanup", true);
      // 0.0.37 (C1): keep the pending-delete side effect; the delete-status /
      // post-delete-count locals were only consumed by the removed pendings
      // gate, so they are dropped to avoid unused-variable warnings.
      DeleteRescuePendings();
      if(pendingActionReady)
         LogRescueStatus("after pending cleanup", true);
      if(bid > 0.0 && ask > 0.0)
        {
         UpdateLegTracking(bid, ask);
         ManagePerLegTrailing(bid, ask);
        }
      TryHarvestRescueHedges();
      TryProfitFundedCleanup("rescue-hold");
      if(bid > 0.0 && ask > 0.0)
        {
         // 0.0.37 (C1): UNGATED from pending-order state, mirroring the 0.0.36
         // fix already applied at ProcessTrendRescue (L~9499) and the normal
         // tail (L~10674) but MISSED here. In rescue hold the EA holds positions
         // while pendings linger, so pendingsAfterDelete>0 is common and the
         // protective hedge was dead-gated exactly when needed. The rescue hedge
         // is an independent net-exposure position and self-gates internally on
         // InpUseRescueHedge / IsHedgingAccount / ACCOUNT_HEDGE_ALLOWED /
         // cooldown / max-hedges / loss-trigger / net-lots / margin, so pending-
         // order state is irrelevant to it.
         if(CanTrade())
            TryOpenRescueHedge(bid, ask);
        }
      }

   if(!g_cycleStartTrusted)
      return;

   double net = CycleNet();
   double closeBuffer = MoneyInput(InpCleanupCostBufferUSD);
   if(net >= closeBuffer)
     {
      ClearRescueHold();
      BeginTearDown(StringFormat("rescue hold safe close, cycleNet %.2f >= close buffer %.2f",
                                 net, closeBuffer),
                    closeBuffer);
     }
  }

//+------------------------------------------------------------------+
//| Delete stale/off-grid Straddle pendings before an active refill.  |
//| This never closes positions. It only removes this EA's symbol+     |
//| magic pending orders whose STR comments no longer match the fixed |
//| grid settings for the current anchor.                             |
//+------------------------------------------------------------------+
int CleanupOffGridPendings()
  {
   if(g_anchor <= 0.0)
      return 0;

   // 1.1.9: keep every valid-looking STR B/S stop on the chart; only remove
   // unparseable / non-stop garbage comments.
   if(InpKeepGridPendings)
     {
      ulong junk[];
      int jn = 0;
      for(int i = OrdersTotal() - 1; i >= 0; i--)
        {
         ulong ticket = OrderGetTicket(i);
         if(ticket == 0)
            continue;
         if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
            continue;
         if(OrderGetString(ORDER_SYMBOL) != g_sym)
            continue;
         string comment = OrderGetString(ORDER_COMMENT);
         ENUM_ORDER_TYPE type = (ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
         // Keep grid STR B#/S# and basket recovery STR RCV stops
         if(IsBasketRecoveryComment(comment) &&
            (type == ORDER_TYPE_BUY_STOP || type == ORDER_TYPE_SELL_STOP))
            continue;
         if(!IsStraddleComment(comment))
            continue;
         bool isBuy = true;
         int lvl = 0;
         if(ParseLegComment(comment, isBuy, lvl) &&
            ((isBuy && type == ORDER_TYPE_BUY_STOP) || (!isBuy && type == ORDER_TYPE_SELL_STOP)))
            continue;
         ArrayResize(junk, jn + 1);
         junk[jn++] = ticket;
        }
      int deleted = 0;
      for(int i = 0; i < jn; i++)
         if(DeleteOnePending(junk[i]))
            deleted++;
      return deleted;
     }

   const double gridMin = GridMinPrice();
   const double gridMax = GridMaxPrice();
   const double tick    = TickSize();
   ulong tickets[];
   int n = 0;

   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;

      string comment = OrderGetString(ORDER_COMMENT);
      if(!IsStraddleComment(comment))
         continue;
      bool remove = false;
      bool isBuy = true;
      int  lvl   = 0;
      if(!ParseLegComment(comment, isBuy, lvl))
         remove = true;
      else if(lvl < 1 || lvl > InpGridLevels)
         remove = true;
      else
        {
         ENUM_ORDER_TYPE type = (ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
         if((isBuy && type != ORDER_TYPE_BUY_STOP) || (!isBuy && type != ORDER_TYPE_SELL_STOP))
            remove = true;
         else
           {
            double price    = OrderGetDouble(ORDER_PRICE_OPEN);
            double expected = LevelPrice(isBuy, lvl);
            if(!SameTickPrice(price, expected))
               remove = true;
            else if(price < gridMin - tick * 0.5 || price > gridMax + tick * 0.5)
               remove = true;
           }
        }

      if(remove)
        {
         ArrayResize(tickets, n + 1);
         tickets[n++] = ticket;
        }
     }

   int deleted = 0;
   for(int i = 0; i < n; i++)
      if(DeleteOnePending(tickets[i]))
         deleted++;

   if(n > 0)
      Log(1, StringFormat("Straddle: stale/off-grid pending cleanup deleted %d of %d orders (gridMin %s, gridMax %s)",
                          deleted, n, DoubleToString(gridMin, g_digits), DoubleToString(gridMax, g_digits)));
   return deleted;
  }

//+------------------------------------------------------------------+
//| 1.1.1: cancel counter-trend grid stop pendings while one-side     |
//| bias is active. Deletes magic+symbol pending STR B#/S# grid stops |
//| on the side opposite the bias only:                               |
//|   bias +1 (uptrend):  delete SELL-side grid pendings              |
//|   bias -1 (downtrend): delete BUY-side grid pendings              |
//|   bias 0: no-op                                                   |
//| Never closes open positions. Log: at most one normal-level line   |
//| per bias state change or every 60s summarizing cancels.           |
//+------------------------------------------------------------------+
int CancelCounterTrendGridPendings()
  {
   // 1.1.9: user wants both sides of the grid to stay on the chart
   if(InpKeepGridPendings)
      return 0;

   const int bias = TrendOneSideBias();
   if(bias == 0)
     {
      g_trendOneSideBiasSeen = 0;
      return 0;
     }

   // bias +1 => cancel sell-side; bias -1 => cancel buy-side
   const bool cancelBuy  = (bias < 0);
   const bool cancelSell = (bias > 0);

   ulong tickets[];
   int n = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;

      string comment = OrderGetString(ORDER_COMMENT);
      bool isBuy = true;
      int  lvl   = 0;
      if(!ParseLegComment(comment, isBuy, lvl))
         continue;
      if(lvl < 1)
         continue;

      ENUM_ORDER_TYPE type = (ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
      if(isBuy && type != ORDER_TYPE_BUY_STOP)
         continue;
      if(!isBuy && type != ORDER_TYPE_SELL_STOP)
         continue;

      if((isBuy && cancelBuy) || (!isBuy && cancelSell))
        {
         ArrayResize(tickets, n + 1);
         tickets[n++] = ticket;
        }
     }

   int deleted = 0;
   for(int i = 0; i < n; i++)
      if(DeleteOnePending(tickets[i]))
         deleted++;

   const bool biasChanged = (bias != g_trendOneSideBiasSeen);
   g_trendOneSideBiasSeen = bias;
   const datetime now = TimeCurrent();
   const bool timeOk = (g_lastCounterTrendCancelLog == 0 ||
                        (now - g_lastCounterTrendCancelLog) >= 60);
   if(deleted > 0 || biasChanged)
     {
      if(biasChanged || timeOk)
        {
         Log(1, StringFormat("Straddle: trend one-side bias=%d cancelled %d counter-trend grid pendings (side=%s)",
                             bias, deleted, (bias > 0 ? "SELL" : "BUY")));
         g_lastCounterTrendCancelLog = now;
        }
      else
         Log(2, StringFormat("Straddle: trend one-side bias=%d cancelled %d counter-trend grid pendings (throttled)",
                             bias, deleted));
     }
   return deleted;
  }

//+------------------------------------------------------------------+
//| GRID POPULATION (0.0.8) - the single rule that does BOTH the      |
//| initial build AND the refill: for every (side, level k) slot that |
//| has NEITHER an open position NOR a pending order (matched by      |
//| magic+symbol+comment), place a stop order at the slot's FIXED     |
//| level price. A slot whose fixed price is not currently a valid    |
//| stop (wrong side of market / inside stops-freeze) is SKIPPED this |
//| tick and retried on later ticks - it is never re-centered.        |
//| Returns the number of orders placed this pass. Invalid STR        |
//| pending comments/prices are cleaned before refill; invalid        |
//| positions keep their tickets but own no slot.                     |
//| 1.1.1: when TrendOneSideBias()!=0, skip the counter-trend side;   |
//| fixed level prices are never re-centered.                         |
//+------------------------------------------------------------------+
int PopulateGrid()
  {
   MarketValidationResetZeroGridTracking();
   // 1.1.18: daily profit/loss limit — no new grid for the rest of the day
   if(IsDailyTradingStopped())
     {
      MarketValidationMarkOtherZeroPlacementCause();
      return 0;
     }
   // 1.1.16: honor margin/populate backoff before walking 120 slots again
   if(g_nextBuildTry > 0 && TimeCurrent() < g_nextBuildTry)
     {
      MarketValidationMarkOtherZeroPlacementCause();
      return 0;
     }
   if(IsStopped())
     {
      g_buildSkipWait = true;
      MarketValidationMarkOtherZeroPlacementCause();
      return 0;
     }
   // 1.1.9 KeepGridPendings: ALWAYS refill empty slots even during rescue/EQ
   // recovery so buy/sell stops never disappear from the chart.
   if(!InpKeepGridPendings)
     {
      if(IsRescueHoldActive() || IsTrendRescueActive())
        {
         MarketValidationMarkOtherZeroPlacementCause();
         return 0;
        }
      if(FloatBagBlocksNewGrid() || EquityRecoveryActive())
        {
         MarketValidationMarkOtherZeroPlacementCause();
         return 0;
        }
     }

   double bid = 0.0, ask = 0.0;
   if(!RefreshCurrentPrices(bid, ask))
     {
      MarketValidationMarkOtherZeroPlacementCause();
      return 0;                                         // market data not ready - defer
     }
   // SYMBOL_TRADE_TICK_VALUE can read 0.0 before the first tick (spec 1.1) -
   // a zero value signals the symbol is not fully initialized; defer.
   if(SymbolInfoDouble(g_sym, SYMBOL_TRADE_TICK_VALUE) <= 0.0)
     {
      MarketValidationMarkOtherZeroPlacementCause();
      return 0;
     }
   if(g_anchor <= 0.0)
     {
      MarketValidationMarkOtherZeroPlacementCause();
      return 0;                                         // no anchor yet (recovery edge)
     }

   // 1.1.1: compute once per pass; 0 = both sides, +1 buy-only, -1 sell-only
   // 1.1.9 keep-pendings: always both sides so chart stays fully populated
   const int oneSideBias = (InpKeepGridPendings ? 0 : TrendOneSideBias());

   // --- slot occupancy from open positions + pendings (comment-matched)
   bool occBuy[], occSell[];
   ArrayResize(occBuy,  InpGridLevels + 1);
   ArrayResize(occSell, InpGridLevels + 1);
   for(int k = 0; k <= InpGridLevels; k++)
     {
      occBuy[k]  = false;
      occSell[k] = false;
     }
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      // A Float ticket keeps its grid comment but must not claim a new-cycle slot.
      if(IsFloatTicket(ticket))
         continue;
      bool isBuy; int lvl;
      if(!ParseLegComment(PositionGetString(POSITION_COMMENT), isBuy, lvl))
         continue;
      if(lvl >= 1 && lvl <= InpGridLevels)
        {
         if(isBuy)
            occBuy[lvl] = true;
         else
            occSell[lvl] = true;
        }
     }
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;
      bool isBuy; int lvl;
      if(!ParseLegComment(OrderGetString(ORDER_COMMENT), isBuy, lvl))
         continue;
      if(lvl >= 1 && lvl <= InpGridLevels)
        {
         if(isBuy)
            occBuy[lvl] = true;
         else
            occSell[lvl] = true;
        }
     }

    // --- fill every empty slot whose FIXED price is a valid stop NOW
    g_abortBuild    = false;
    g_marginAbortBuild = false;
    g_marginAbortLevel = 0;
    g_buildSkipWait = false;
   double minDist  = MinStopDistance();

   int placed = 0;
   // interleave buy/sell per level so a hard-limit abort degrades symmetrically
   // 1.1.14: always-visible TREND-2X status + upgrade stuck 1x with-trend pendings
   LogTrendDoubleLotStatus();
   if(UpgradeTrendDoubleLotPendings() > 0)
     {
      // Recompute occupancy after deletes so this same pass can refill at 2x
      for(int k = 0; k <= InpGridLevels; k++)
        {
         occBuy[k]  = false;
         occSell[k] = false;
        }
      for(int i = PositionsTotal() - 1; i >= 0; i--)
        {
         ulong ticket = PositionGetTicket(i);
         if(ticket == 0)
            continue;
         if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
            continue;
         if(PositionGetString(POSITION_SYMBOL) != g_sym)
            continue;
         if(IsFloatTicket(ticket))
            continue;
         bool isBuy; int lvl;
         if(!ParseLegComment(PositionGetString(POSITION_COMMENT), isBuy, lvl))
            continue;
         if(lvl >= 1 && lvl <= InpGridLevels)
           {
            if(isBuy)
               occBuy[lvl] = true;
            else
               occSell[lvl] = true;
           }
        }
      for(int i = OrdersTotal() - 1; i >= 0; i--)
        {
         ulong ticket = OrderGetTicket(i);
         if(ticket == 0)
            continue;
         if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
            continue;
         if(OrderGetString(ORDER_SYMBOL) != g_sym)
            continue;
         bool isBuy; int lvl;
         if(!ParseLegComment(OrderGetString(ORDER_COMMENT), isBuy, lvl))
            continue;
         if(lvl >= 1 && lvl <= InpGridLevels)
           {
            if(isBuy)
               occBuy[lvl] = true;
            else
               occSell[lvl] = true;
           }
        }
     }

    for(int k = 1; k <= EffectiveGridLevels() && !g_abortBuild && !g_buildSkipWait && !IsStopped(); k++)
      {
       bool buyPlacedForPair = false;
       const bool emptyBuyPair = (!occBuy[k] && oneSideBias >= 0);
       const bool emptySellPair = (!occSell[k] && oneSideBias <= 0);
       if(emptyBuyPair && emptySellPair)
         {
          const double pairBuyRequested = GridLotForLevel(k, true);
          const double pairSellRequested = GridLotForLevel(k, false);
          const double pairBuyLots = NormalizeLot(pairBuyRequested);
          const double pairSellLots = NormalizeLot(pairSellRequested);
          const bool pairLotsSafe = (pairBuyLots > 0.0 && pairSellLots > 0.0 &&
                                     MarketValidationGridLotSafe(pairBuyRequested, pairBuyLots, k) &&
                                     MarketValidationGridLotSafe(pairSellRequested, pairSellLots, k));
          if(pairLotsSafe && !MarketValidationPairMarginSafe(k, pairBuyLots, pairSellLots))
            {
             g_abortBuild = true;
             g_marginAbortBuild = true;
             g_marginAbortLevel = k;
             const datetime until = TimeCurrent() + InpRetrySeconds;
             if(until > g_nextBuildTry)
                g_nextBuildTry = until;
             break;
            }
         }

       // 1.1.1: skip buy placement during downtrend one-side bias (bias < 0)
       if(!occBuy[k] && oneSideBias >= 0)
        {
         double requestedLots = GridLotForLevel(k, true);
         double lots = NormalizeLot(requestedLots);
         if(lots <= 0.0)
           {
            MarketValidationMarkOtherZeroPlacementCause();
           }
         else if(!MarketValidationGridLotSafe(requestedLots, lots, k))
           {
            // skip
           }
         else
           {
            // valid BUY STOP only above ask + stops/freeze distance; otherwise
            // SKIP this tick (do NOT re-center) and retry on later ticks
            double buyPrice = LevelPrice(true, k);
            if(buyPrice >= SnapUp(ask + minDist))
              {
                if(PlaceStopOrder(true, lots, k))
                  {
                   placed++;
                   buyPlacedForPair = true;
                  }
               else
                  MarketValidationMarkOtherZeroPlacementCause();
              }
            else
               MarketValidationMarkOtherZeroPlacementCause();
           }
        }
       if(g_abortBuild || g_buildSkipWait || IsStopped())
         {
          if(buyPlacedForPair && g_marginAbortBuild)
            {
             if(DeleteGridPendingSlot(true, k))
                placed--;
             else
                LogOp(StringFormat("Straddle: WARN could not roll back BUY STOP L%d after a margin-aborted pair build",
                                   k));
            }
          break;
         }
      // 1.1.1: skip sell placement during uptrend one-side bias (bias > 0)
      if(!occSell[k] && oneSideBias <= 0)
        {
         double requestedLots = GridLotForLevel(k, false);
         double lots = NormalizeLot(requestedLots);
         if(lots <= 0.0)
           {
            MarketValidationMarkOtherZeroPlacementCause();
           }
         else if(!MarketValidationGridLotSafe(requestedLots, lots, k))
           {
            // skip
           }
         else
           {
            double sellPrice = LevelPrice(false, k);
            if(sellPrice <= SnapDown(bid - minDist))
              {
               if(PlaceStopOrder(false, lots, k))
                  placed++;
               else
                  MarketValidationMarkOtherZeroPlacementCause();
              }
            else
               MarketValidationMarkOtherZeroPlacementCause();
           }
         }
       if(buyPlacedForPair && g_marginAbortBuild)
         {
          if(DeleteGridPendingSlot(true, k))
             placed--;
          else
             LogOp(StringFormat("Straddle: WARN could not roll back BUY STOP L%d after a margin-aborted pair build",
                                k));
         }
      }
    if(IsStopped())
     {
      g_buildSkipWait = true;
      MarketValidationMarkOtherZeroPlacementCause();
     }
   if(g_abortBuild || g_buildSkipWait)
      MarketValidationMarkOtherZeroPlacementCause();
   return placed;
  }

//+------------------------------------------------------------------+
//| Start a fresh cycle when flat (0 positions AND 0 pendings),       |
//| tradeable and not paused: set the FIXED anchor to the snapped     |
//| mid, stamp the cycle start (realized-P/L window), clear per-leg   |
//| tracking, then let the population rule place the full grid (all   |
//| 24 slots are empty -> full build). A start that places 0 orders   |
//| is NOT a cycle: state is undone and a back-off window armed so    |
//| flat-detection does not re-fire the build on every tick.          |
//+------------------------------------------------------------------+
void StartCycle()
  {
   if(IsStopped())
      return;
   if(IsDailyTradingStopped())
     {
      LogState(1, "daily-stop-start",
               StringFormat("Straddle: DAILY STOP - no new cycle (%s) dayPL=%.2f",
                            g_dailyStopReason, g_dailyEquityPL),
               300);
      return;
     }
   if(IsRescueHoldActive() || IsTrendRescueActive())
      return;
   // When KeepGridPendings and we already have a live fixed grid, only refill
   // empty slots — never re-center anchor (would orphan chart stops).
   if(InpKeepGridPendings && g_anchor > 0.0 && CountMyPendings() > 0)
     {
      int placedKeep = PopulateGrid();
      if(placedKeep > 0)
         Log(1, StringFormat("Straddle: grid refill (keep-pendings) placed %d stops, anchor %s, pendings now %d",
                             placedKeep, DoubleToString(g_anchor, g_digits), CountMyPendings()));
      return;
     }
   // 1.1.5–1.1.8: no fresh re-anchor while float bag red OR equity recovery
   // (but keep-mode refill above still runs).
   if(!InpKeepGridPendings && (FloatBagBlocksNewGrid() || EquityRecoveryActive()))
     {
      LogState(1, "cycle-block-float",
               StringFormat("Straddle: ERR no-new-grid f=%d pl=%.0f eqRec=%d",
                            g_floatCount, FloatFloatingPL(), (EquityRecoveryActive() ? 1 : 0)),
               300);
      return;
     }

   double bid = 0.0, ask = 0.0;
   if(!RefreshCurrentPrices(bid, ask))
      return;                                           // market data not ready - defer
   if(SymbolInfoDouble(g_sym, SYMBOL_TRADE_TICK_VALUE) <= 0.0)
      return;

   g_anchor     = SnapPrice((bid + ask) / 2.0);         // FIXED for the whole cycle
   SnapshotCycleGridStepDistance();                     // FIXED for the whole cycle
   g_cycleStart = TimeCurrent();
   g_cycleStartTrusted = true;
   PersistCycleStart();
   // 1.1.14: fresh cycle — TREND-2X recovery latch starts clean
   g_trend2xArmedThisCycle = false;
   g_trend2xWasActive = false;
   g_trend2xLastDir = 0;
   // 0.0.44: bound the float-closed-id set - drop ids whose OUT deal predates the new
   // cycle start (they can no longer enter CycleRealized's window). No-op default-off.
   if(InpUseFloatReanchor && g_floatClosedCount > 0)
      PruneFloatClosedPositionIds(g_cycleStart);
   ArrayResize(g_legs, 0);                              // clear per-position tracking

   // 1.1.1: if one-side bias is already active at cycle start, cancel any
   // residual counter-trend grid pendings before the initial PopulateGrid.
   // (Flat start normally has no pendings; call is required for bias!=0.)
   if(TrendOneSideBias() != 0)
      CancelCounterTrendGridPendings();

   int placed = PopulateGrid();
   if(IsStopped())
     {
      if(placed <= 0)
         ResetCycleState();
      return;
     }
   if(placed > 0)
     {
      double gridMin = GridMinPrice();
      double gridMax = GridMaxPrice();
      string note = "";
       if(g_marginAbortBuild)
          note = StringFormat(" (margin capped before level %d; BUY/SELL pairs kept symmetric)",
                              g_marginAbortLevel);
       else if(g_abortBuild)
          note = " (degraded by broker order limit)";
      else if(g_buildSkipWait)
         note = " (partial - trading became unavailable)";
      else if(placed < 2 * EffectiveGridLevels())
         note = " (partial - skipped slots retry per tick)";
      if(IsStopped())
        {
         return;
        }
      LogAlways(StringFormat("Straddle: cycle started, anchor %s, gridMin %s, gridMax %s - %d of %d stop orders placed%s",
                             DoubleToString(g_anchor, g_digits),
                             DoubleToString(gridMin, g_digits),
                             DoubleToString(gridMax, g_digits),
                             placed, 2 * EffectiveGridLevels(), note));
     }
   else
     {
      if(MarketValidationFallbackEligibleAfterZeroGrid(placed))
        {
         if(TryMarketValidationFallbackTrade())
            return;
        }
      // 0-order start: NOT a cycle - undo state, arm back-off, one debug line
      ResetCycleState();
      g_nextBuildTry = TimeCurrent() + InpRetrySeconds;
      Log(2, StringFormat("Straddle: cycle start placed 0 orders%s - next attempt in %d s",
                          (g_buildSkipWait ? " (market closed / trading disabled)" : ""),
                          InpRetrySeconds));
     }
  }

//+------------------------------------------------------------------+
//| Index of a ticket in the per-leg tracking array, or -1            |
//+------------------------------------------------------------------+
int FindLeg(const ulong ticket)
  {
   for(int i = ArraySize(g_legs) - 1; i >= 0; i--)
      if(g_legs[i].ticket == ticket)
         return i;
   return -1;
  }

int FindTrailModifyThrottle(const ulong ticket)
  {
   for(int i = ArraySize(g_trailModifyThrottle) - 1; i >= 0; i--)
      if(g_trailModifyThrottle[i].ticket == ticket)
         return i;
   return -1;
  }

void RemoveTrailModifyThrottle(const ulong ticket)
  {
   int idx = FindTrailModifyThrottle(ticket);
   if(idx >= 0)
      ArrayRemove(g_trailModifyThrottle, idx, 1);
  }

//+------------------------------------------------------------------+
//| Drop a ticket from the per-leg tracking array (if present)        |
//+------------------------------------------------------------------+
void RemoveLeg(const ulong ticket)
  {
   int idx = FindLeg(ticket);
   if(idx >= 0)
      ArrayRemove(g_legs, idx, 1);
   RemoveTrailModifyThrottle(ticket);
  }

void RecordTrailModifySuccess(const ulong ticket, const double sl)
  {
   int idx = FindTrailModifyThrottle(ticket);
   if(idx < 0)
     {
      idx = ArraySize(g_trailModifyThrottle);
      ArrayResize(g_trailModifyThrottle, idx + 1);
      g_trailModifyThrottle[idx].ticket = ticket;
     }
   g_trailModifyThrottle[idx].lastSuccess = TimeCurrent();
   g_trailModifyThrottle[idx].lastSL = NormalizeDouble(sl, g_digits);
  }

double TrailModifyMinStepDistance()
  {
   if(InpTrailModifyMinStepUSD <= 0.0)
      return 0.0;
   return PriceDistanceInput(InpTrailModifyMinStepUSD);
  }

bool ShouldSkipTrailModifyPreCall(const ulong ticket,
                                  const bool isBuy,
                                  const double validSL)
  {
   if(!TesterLowLogActive())
      return false;

   int idx = FindTrailModifyThrottle(ticket);
   if(idx < 0 || g_trailModifyThrottle[idx].lastSuccess <= 0 ||
      g_trailModifyThrottle[idx].lastSL <= 0.0)
      return false;

   int minSeconds = InpTrailModifyMinSeconds;
   if(minSeconds < 0)
      minSeconds = 0;
   double minStep = TrailModifyMinStepDistance();
   datetime now = TimeCurrent();
   bool enoughTime = (minSeconds <= 0 ||
                      (now - g_trailModifyThrottle[idx].lastSuccess) >= minSeconds);
   double improvement = (isBuy
                         ? validSL - g_trailModifyThrottle[idx].lastSL
                         : g_trailModifyThrottle[idx].lastSL - validSL);
   bool enoughStep = (minStep <= 0.0 || improvement + 1.0e-8 >= minStep);

   if(enoughTime || enoughStep)
      return false;

   g_lowLogSuppressedModifyPrecall++;
   return true;
  }

//+------------------------------------------------------------------+
//| Per-leg tracking maintenance (0.0.8), every tick:                 |
//|   - add newly-filled positions (init peak = current favorable     |
//|     CLOSE price: buy -> bid, sell -> ask)                         |
//|   - ratchet each tracked peak (buy: max bid, sell: min ask)       |
//|   - prune tickets whose position is gone                          |
//+------------------------------------------------------------------+
void UpdateLegTracking(const double bid, const double ask)
  {
   int  n0 = ArraySize(g_legs);
   bool seen[];
   ArrayResize(seen, n0);
   for(int i = 0; i < n0; i++)
      seen[i] = false;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      bool   isBuy = ((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY);
      double fav   = (isBuy ? bid : ask);               // favorable CLOSE price now
      int    idx   = FindLeg(ticket);
      if(idx >= 0)
        {
         if(idx < n0)
            seen[idx] = true;
         g_legs[idx].peak = (isBuy ? MathMax(g_legs[idx].peak, fav)
                                   : MathMin(g_legs[idx].peak, fav));
        }
      else
        {
         int sz = ArraySize(g_legs);
         ArrayResize(g_legs, sz + 1);
         g_legs[sz].ticket = ticket;
         g_legs[sz].peak   = fav;
        }
     }

   // prune gone tickets (appended entries live past index n0 - untouched)
   for(int i = n0 - 1; i >= 0; i--)
      if(!seen[i])
        {
         RemoveTrailModifyThrottle(g_legs[i].ticket);
         ArrayRemove(g_legs, i, 1);
        }
  }

//+------------------------------------------------------------------+
//| PER-LEG BROKER-SIDE TRAILING SL (1.1.15 PRICE-GRID TRAIL)         |
//| InpTrailArmSteps = number of GRID STEPS of favorable price move  |
//| from ENTRY required to ARM trailing (NOT count of open tickets). |
//| Example: Arm=5, GridStepUSD=1 => arm after +$5 move; first SL    |
//| sits Arm grids behind peak (5-point trail starts).               |
//| InpTrailStepN = trail distance after further move (2 or 1).      |
//| Progressive: while profit is still near arm, use Arm distance;   |
//| once price runs further, tighten to TrailStepN.                  |
//| Only IN-PROFIT legs; losing legs untouched.                      |
//+------------------------------------------------------------------+
void ManagePerLegTrailing(const double bid, const double ask)
  {
   if(!InpUseTrailing)
      return;

   // Optional gate (default OFF): only trail when equity is green.
   if(InpTrailOnlyIfEquityGreen)
     {
      double bal = AccountInfoDouble(ACCOUNT_BALANCE);
      double eq  = AccountInfoDouble(ACCOUNT_EQUITY);
      double need = bal + MoneyInput(InpTrailMinEquityLeadUSD);
      if(eq + 1.0e-6 < need)
         return;
     }

   const double gridStep = GridStepDistance();
   if(gridStep <= 0.0)
      return;
   const int armN = MathMax(1, InpTrailArmSteps);
   const int stepN = MathMax(1, InpTrailStepN);
   const double armDist  = (double)armN * gridStep;   // e.g. 5 * $1
   const double stepDist = (double)stepN * gridStep;  // e.g. 2 * $1

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      bool isBuy = ((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY);
      string comment = PositionGetString(POSITION_COMMENT);
      if(IsAveragingComment(comment))
         continue;                                      // never trail averaging adds
      if(PositionGetDouble(POSITION_PROFIT) <= 0.0)
         continue;                                      // only IN-PROFIT legs
      int idx = FindLeg(ticket);
      if(idx < 0)
         continue;                                      // not tracked yet

      if(!g_symbol.RefreshRates())
         continue;
      double freshBid = g_symbol.Bid();
      double freshAsk = g_symbol.Ask();
      if(freshBid <= 0.0 || freshAsk <= 0.0)
         continue;

      g_legs[idx].peak = (isBuy ? MathMax(g_legs[idx].peak, freshBid)
                                : MathMin(g_legs[idx].peak, freshAsk));

      double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      double currentSL = PositionGetDouble(POSITION_SL);
      double tick      = TickSize();
      double minDist   = MinStopDistance();

      // Favorable move from entry to peak (price units)
      const double peak = g_legs[idx].peak;
      const double profitMove = (isBuy ? (peak - openPrice) : (openPrice - peak));
      if(profitMove + 1.0e-8 < armDist)
         continue; // not yet +Arm grids in profit — no SL yet

      // Progressive trail distance:
      //  - just armed: use Arm grids (first 5-point trail)
      //  - further run: tighten to TrailStepN (2 or 1)
      double trailDist = armDist;
      if(profitMove + 1.0e-8 >= armDist + stepDist)
         trailDist = stepDist;

      double rawSL = (isBuy ? peak - trailDist : peak + trailDist);
      double candidateSL = NormalizeDouble((isBuy ? SnapDown(rawSL) : SnapUp(rawSL)), g_digits);

      if(candidateSL <= 0.0)
         continue;

      if(isBuy)
        {
         double minProfitSL = SnapUp(openPrice + tick);
         double maxValidSL  = SnapDown(freshBid - minDist);
         if(candidateSL < minProfitSL || candidateSL > maxValidSL || candidateSL >= freshBid)
            continue;
         if(currentSL > 0.0 && candidateSL <= currentSL + tick * 0.5)
            continue;
        }
      else
        {
         double maxProfitSL = SnapDown(openPrice - tick);
         double minValidSL  = SnapUp(freshAsk + minDist);
         if(candidateSL > maxProfitSL || candidateSL < minValidSL || candidateSL <= freshAsk)
            continue;
         if(currentSL > 0.0 && candidateSL >= currentSL - tick * 0.5)
            continue;
        }

      double validSL = candidateSL;
      if(IsStopped())
         break;
      if(!PositionSelectByTicket(ticket))
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(PositionGetDouble(POSITION_VOLUME) <= 0.0)
         continue;
      if(((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) != isBuy)
         continue;
      double freshProfitAndSwap = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(freshProfitAndSwap <= 0.0)
         continue;

      double freshSL = PositionGetDouble(POSITION_SL);
      double freshTp = PositionGetDouble(POSITION_TP);
      if(isBuy)
        {
         if(freshSL > 0.0 && validSL <= freshSL + tick * 0.5)
            continue;
        }
      else
        {
         if(freshSL > 0.0 && validSL >= freshSL - tick * 0.5)
            continue;
        }

      if(ShouldSkipTrailModifyPreCall(ticket, isBuy, validSL))
         continue;

      StrActionOutcome outcome=ExecutePositionProtection(ticket,validSL,freshTp,"STR TRAIL");
      uint rc=outcome.retcode;
      if(outcome.state==COMPLETED)
        {
         // 0.0.40 O1: PositionModify only changes SL/TP - it does NOT alter any
         // cached aggregate (count/volume/PROFIT/SWAP). Marking dirty here is
         // OPTIONAL and harmless (one extra recompute, value identical); kept
         // for a uniform "every trade op dirties" rule.
         g_bookDirty = true;
         RecordTrailModifySuccess(ticket, validSL);
         string lowLogKey = StringFormat("position-modified|ticket=%I64u", ticket);
         if(!TesterLowLogActive() || !InpSuppressSuccessfulTradeLogs ||
            ShouldEmitLog(2, lowLogKey, lowLogKey))
            Log(2, StringFormat("Straddle: leg %s broker-side SL modified to %s",
                                PositionGetString(POSITION_COMMENT), DoubleToString(validSL, g_digits)));
         continue;
        }

      LogOp(StringFormat("Straddle: PositionModify trailing SL for position #%I64u failed, retcode %u (%s), comment '%s'",
                         ticket,rc,outcome.result_comment,outcome.result_comment));
     }
  }

//+------------------------------------------------------------------+
//| Begin closing the whole cycle. Sets the PERSISTENT teardown flag  |
//| (runtime g_tearingDown AND the terminal global-variable marker,   |
//| which survives recompile/VPS restart) so the close/delete is      |
//| retried until the EA is truly flat - even if a close or delete    |
//| fails mid-way, a partial close drops the recomputed net back      |
//| below target, or the terminal restarts mid-teardown.              |
//| 0.0.10: this is the shared whole-grid teardown path for net       |
//| target and strict grid-boundary exits.                            |
//| 0.0.38: isBasket tags the teardown as basket-take-profit-initiated |
//| so ProcessTearDown re-checks total floating >= +threshold (rather  |
//| than the weaker CycleNet>=0 guard) before closing on a resume /    |
//| partial-close / restart. Default false keeps every existing caller |
//| behaving exactly as in 0.0.37 and clears any stale basket tag.     |
//+------------------------------------------------------------------+
void BeginTearDown(const string reason, const double safetyThreshold, const bool isBasket = false)
  {
   int positions = CountMyPositions();
   if(!EnsureTrustedCycleStartForClose(reason, positions))
      return;   // diverted to EnterRescueHold, which has cleared any basket tag - safe deferral, never a loss

   LogAlways("Straddle: closing cycle - " + reason);
   g_tearingDown   = true;
   // 0.0.38: set/clear the basket tag AFTER the trusted-start check passes so a
   // divert leaves it cleared; default false guarantees a non-basket teardown
   // never inherits a stale basket guard.
   g_basketTearDown = isBasket;
   if(isBasket)
      PersistBasketTearDownTag();
   else
      ClearPersistedBasketTearDownTag();
   g_rescueHolding = false;
   ClearTrendRescueState(false);
   g_teardownSafeThreshold = MathMax(0.0, safetyThreshold);
   GlobalVariableDel(RHVar());
   ClearRescueAnchorBalance();
   ClearRescueHedgeTime();
   if(!GlobalVariableSet(TDVar(), (double)TimeCurrent()))   // stamp = teardown start time
      Log(0, "Straddle: WARN could not set teardown marker");
   PersistTearDownThreshold();
   ProcessTearDown();
  }

//+------------------------------------------------------------------+
//| One teardown attempt: close all positions, delete all pendings.   |
//| Gated by CanTrade(): into a closed/disabled market NO requests    |
//| are sent and a back-off (g_nextTearTry, InpRetrySeconds) is armed |
//| - g_tearingDown stays set so teardown RESUMES at market reopen.   |
//| 0.0.5 NO-PROGRESS BACK-OFF: when an attempt on an OPEN market     |
//| reduces nothing (deterministic close/delete failures such as      |
//| 10013/10014/10044/10045), the back-off is armed too, so a STUCK   |
//| teardown re-attempts at most once per InpRetrySeconds - bounding  |
//| BOTH the Trades-journal request storm and the log - while a       |
//| teardown that IS making progress still retries on the next tick.  |
//| Clears the flag (and the persisted marker) and arms the rebuild   |
//| pause only when TRULY FLAT (zero positions AND zero pendings).    |
//+------------------------------------------------------------------+
void ProcessTearDown()
  {
   int positionCount = CountMyPositions();
   // 0.0.38: BASKET-TEARDOWN RESUME GUARD (closes the only realizable-loss window).
   // A teardown ARMED BY THE BASKET take-profit must complete ONLY while the WHOLE
   // book is still floating >= +threshold. Same-tick this is already true (the gate
   // just saw it, price is frozen, no tick yield). But if this attempt is a RESUME
   // on a LATER tick (market reopened after CanTrade() back-off, or a partial-close
   // retry) OR a restart, floating may have DRIFTED. The generic guard below is
   // CycleNet()=realized+floating >= g_teardownSafeThreshold, which is WEAKER: it can
   // permit closing at NEGATIVE floating whenever realized is positive. So for a
   // tagged basket teardown we re-read OpenFloatingPL FRESH and HOLD (do NOT close,
   // do NOT realize) unless floating is STILL >= +threshold - keeping g_tearingDown
   // set so it re-arms next tick when the book is green again. This makes the
   // no-loss + ">=+threshold" promise hold across resume / restart / partial close.
   double basketTPThreshold = 0.0;
   bool   basketGuardActive = (g_basketTearDown && InpBasketTakeProfitUSD > 0.0);
   if(basketGuardActive)
     {
      basketTPThreshold = MoneyInput(InpBasketTakeProfitUSD);
      if(basketTPThreshold <= 0.0)        // defensive: degenerate money scale -> never close at a loss; treat as a generic teardown
         basketGuardActive = false;
     }
   if(basketGuardActive && positionCount > 0)
     {
      if(!EnsureTrustedCycleStartForClose("basket take-profit close-all guard", positionCount))
         return;

      double basketLots = 0.0;
      double basketFloating = OpenFloatingPL(basketLots);
      if(basketFloating < basketTPThreshold)
        {
         g_nextTearTry = TimeCurrent() + InpRetrySeconds;   // back off; re-probe when green again
         Log(2, StringFormat("Straddle: basket take-profit teardown HELD - total floating %.2f below +%.2f, not closing (no-loss guard), will re-check",
                             basketFloating, basketTPThreshold));
         return;                                            // g_tearingDown + g_basketTearDown stay set
        }
     }
   // 1.1.20: daily stop-and-close — force flatten even if cycleNet is red
   if(g_dailyStopForceClose && positionCount > 0)
     {
      // no EnsureTrusted / no cycleNet rescue hold
     }
   else if(positionCount > 0)
     {
      if(!EnsureTrustedCycleStartForClose("teardown close-all guard", positionCount))
         return;

      double threshold = MathMax(0.0, g_teardownSafeThreshold);
      double teardownNet = CycleNet();
      if(teardownNet < threshold)
        {
         EnterRescueHold(StringFormat("teardown rescue hold - cycleNet %.2f below close safety threshold %.2f, deleting pendings only, holding positions; balance protected but equity risk remains",
                                      teardownNet, threshold));
         return;
        }
     }

   int before = CountMyPositions() + CountMyPendings();
   // 0.0.34: delete pendings FIRST so no live grid stop can be triggered by an
   // adverse tick during the synchronous position-close loop (teardown cancel-race).
   DeleteAllPendings();                                  // STEP 1: remove trigger surface
   // 0.0.41 FIX B: a tagged basket teardown passes the mid-loop no-loss guard so
   // a LIVE adverse spike between two ticket closes can never bank the remaining
   // red legs below +threshold; non-basket teardowns pass false (default) and are
   // bit-for-bit 0.0.40.
   CloseAllPositions(basketGuardActive, basketTPThreshold); // STEP 2: close positions
   // STEP 3: a stop that triggered in the broker before the delete landed leaves a
   // same-tick residual fill. Sweep it in THIS attempt ONLY while still net-safe;
   // a below-threshold spike fill is left for the next-tick safe-guard, which
   // diverts to EnterRescueHold (adoption) rather than locking an arbitrary loss.
   // 0.0.38: for a basket teardown the residual sweep is additionally gated on the
   // FRESH total floating still being >= +threshold, so a same-tick spike fill is
   // never banked below the basket promise.
   // 0.0.42 FIX (round 1, REVIEW blocker): for a NON-basket teardown the orphan
   // bucket is DELIBERATELY left open by CloseAllPositions' close-filter
   // (excludeOrphan == legacy Orphan master && !basketGuardActive). So the residual
   // sweep must be CYCLE-SCOPED: it may only re-sweep a NON-orphan residual fill,
   // never the orphan (which is not a residual, it is the intended remainder). We
   // mirror the close-filter's exact predicate; default-OFF (legacy Orphan master
   // false) keeps cyclePositionsAfter == CountMyPositions() (byte-identical 0.0.41).
   // 0.0.44 FIX (round 2, REVIEW blocker): widen to (legacy Orphan master||
   // InpUseFloatReanchor) so a non-basket target teardown with FLOATED legs is
   // ALSO cycle-scoped, mirroring the CloseAllPositions close-filter at :11228.
   // CloseAllPositions SKIPS floated legs (no-realize invariant), so a whole-book
   // residual/completion test would STILL count them, g_tearingDown would stick
   // forever and the flagship float-only config (InpUseFloatReanchor=true,
   // legacy Orphan master=false) would brick the EA every tick (CheckBasketTakeProfit/
   // FloatGreenClose/EquityHedgeBackstop all unreachable -> floating loss uncapped).
   // Reduces to legacy Orphan master alone when InpUseFloatReanchor=false
   // (default-off byte-identical to 0.0.41).
   bool excludeFloatTD=(InpUseFloatReanchor && !basketGuardActive && !g_dailyStopForceClose);
   int cyclePositionsAfter=CountMyPositions()-(excludeFloatTD ? CountFloatPositions() : 0);
   if(cyclePositionsAfter > 0)
     {
      // 1.1.20 daily stop: always re-sweep residuals (force flat)
      bool residualOk = g_dailyStopForceClose;
      if(!residualOk)
        {
         double residualNet = CycleNet();
         residualOk = (residualNet >= MathMax(0.0, g_teardownSafeThreshold));
        }
      if(residualOk && basketGuardActive)
        {
         double residualLots = 0.0;
         double residualFloating = OpenFloatingPL(residualLots);
         residualOk = (residualFloating >= basketTPThreshold);
        }
      if(residualOk)
         CloseAllPositions(basketGuardActive, basketTPThreshold); // 0.0.41 FIX B: same mid-loop no-loss guard on the residual sweep
     }
   // 0.0.42 FIX (round 1, REVIEW blocker): COMPLETION must be CYCLE-SCOPED for a
   // non-basket teardown. CloseAllPositions' close-filter intentionally leaves the
   // orphan bucket OPEN (excludeOrphan == legacy Orphan master && !basketGuardActive),
   // so a whole-book `after` would STILL count the stranded orphan, `after==0` would
   // never hold, g_tearingDown would stick forever and no fresh cycle would ever
   // start (the EA would hang on the first fresh-cycle bank while an orphan exists).
   // We subtract the orphan bucket under the SAME predicate as the close-filter so an
   // orphan-ONLY remainder counts as FLAT and the teardown completes -> ResetCycleState
   // runs, a fresh cycle restarts, and the orphan is left for change 16's green-close.
   // Default-OFF (legacy Orphan master false): orphan counts are 0 and `after` is
   // byte-identical to 0.0.41 (CountMyPositions()+CountMyPendings()).
   int floatRemainder=(excludeFloatTD ? CountFloatPositions() : 0);
   int after=(CountMyPositions()+CountMyPendings())-floatRemainder;

   if(after == 0)
     {
      const bool wasDailyStop = g_dailyStopForceClose;
      g_tearingDown = false;
      g_dailyStopForceClose = false;
      g_pauseUntil  = TimeCurrent() + InpPauseSeconds;
      ResetCycleState();                                // next cycle gets a fresh anchor/start
      GlobalVariableDel(TDVar());                       // clear the persisted teardown marker
       ClearPersistedTearDownThreshold();
       ClearPersistedBasketTearDownTag();                // 0.0.38: basket teardown completed -> drop the tag
       if(wasDailyStop)
          ClearPersistedDailyStopMarker();               // 1.1.23: daily force-close marker ends only when truly flat
       GlobalVariableDel(RHVar());                       // clear any stale rescue marker
      ClearRescueAnchorBalance();
      ClearRescueHedgeTime();
      ClearTrendRescueState(false);
      // 0.0.41 FIX C1: a completed cycle must not leave a stale averaging price/
      // time GV that a NEW episode would wrongly treat as its adverse-step
      // reference. Gated on InpUseBasketAveraging so a default-OFF run never
      // touches a GlobalVariable here (byte-identical to 0.0.40).
      if(InpUseBasketAveraging)
         ClearBasketAvgState();
      if(wasDailyStop && g_dailyTradingStopped)
         if(IsLossCooldownActive(DailyDayStamp(TimeCurrent())))
            LogAlways(StringFormat(
               "Straddle: DAILY STOP complete - book FLAT; no new trades until %s",
               TimeToString(g_lossCooldownResumeDay, TIME_DATE)));
         else
            LogAlways("Straddle: DAILY STOP complete - book FLAT; no new trades until next day");
      else
         LogAlways(StringFormat("Straddle: cycle flat, rebuilding after %d s pause", InpPauseSeconds));
     }
   else
     {
      if(after >= before)
         g_nextTearTry = TimeCurrent() + InpRetrySeconds;   // stuck -> back off; progress -> next-tick retry
      Log(2, "Straddle: cycle close incomplete - retrying");
     }
  }

void BeginTearDownOrRescue(const string reason)
  {
   int positions = CountMyPositions();
   if(positions > 0)
     {
      if(!EnsureTrustedCycleStartForClose(reason, positions))
         return;

      double net = CycleNet();
      double closeBuffer = MoneyInput(InpCleanupCostBufferUSD);
      if(net < closeBuffer)
        {
         EnterRescueHold(StringFormat("%s, cycleNet %.2f below close buffer %.2f, deleting pendings only, holding positions; balance protected but equity risk remains",
                                      reason, net, closeBuffer));
         return;
        }
     }

   BeginTearDown(reason, MoneyInput(InpCleanupCostBufferUSD));
  }

//+------------------------------------------------------------------+
//| 0.0.38: ACCOUNT-LEVEL BASKET TAKE-PROFIT.                          |
//| State-agnostic: fires the instant the TOTAL floating P/L of the   |
//| whole EA book (magic+symbol: orphans, grid, STR TRB/TRS, RHG       |
//| hedge) first reaches >= +MoneyInput(InpBasketTakeProfitUSD),       |
//| regardless of normal-cycle / trend-rescue / rescue-hold state.     |
//| STRICT NO-LOSS INVARIANT: the gate is floating >= +threshold with  |
//| the (scaled) threshold strictly > 0, so this path can NEVER close  |
//| the book at a negative total. It reuses OpenFloatingPL (the exact  |
//| swap- and commission-adjusted whole-book net the EA banks on) and  |
//| the proven clean close+reset path BeginTearDown -> ProcessTearDown.|
//| The teardown is TAGGED (g_basketTearDown + persisted BTVar) so the |
//| resume path re-checks floating>=+threshold before ever closing.    |
//| At default 0.0 this returns on the first guard: zero behavioral    |
//| diff vs 0.0.37 (no added trades, no log lines).                    |
//+------------------------------------------------------------------+
void CheckBasketTakeProfit()
  {
   if(InpBasketTakeProfitUSD <= 0.0)      // OFF -> bit-for-bit 0.0.37
      return;
   if(g_tearingDown)                       // already closing -> no double-teardown / no re-entrancy
      return;
   int positions = CountMyPositions();
   if(positions <= 0)                      // nothing to bank; never tear down an empty book
      return;
   double threshold = MoneyInput(InpBasketTakeProfitUSD);
   if(threshold <= 0.0)                    // defensive: scaled threshold must stay > 0 (cent-account / degenerate scale)
      return;
   double openLots = 0.0;
   double floating = OpenFloatingPL(openLots);   // ALL positions, swap + commission adjusted
   if(floating < threshold)                // STRICT no-loss gate: fire ONLY when the whole book is GREEN by >= +threshold
      return;

   // Arm as a BASKET teardown (isBasket=true): BeginTearDown sets g_basketTearDown +
   // persists the BT marker AFTER its trusted-start check, so a market-closed resume /
   // partial-close retry / restart stays under the floating>=+threshold close guard.
   BeginTearDown(StringFormat("basket take-profit: total floating %.2f >= +%.2f, banking whole book (positions %d, lots %.2f)",
                              floating, threshold, positions, openLots),
                 0.0,    // safetyThreshold 0.0: the basket resume guard (floating>=+threshold) governs the close, not CycleNet
                 true);  // isBasket
   // If BeginTearDown diverted to EnterRescueHold (untrusted cycle start), it has
   // already cleared g_basketTearDown + the BT marker; basket-TP simply DEFERS until
   // the anchor is re-trusted (safe, never a loss). It is NOT bypassed by design.
  }

//+------------------------------------------------------------------+
//| Core state machine - called from OnTick and the OnTimer net.      |
//| 0.0.11 active-cycle order of operations:                          |
//|   1. Persistent teardown continuation always wins.                |
//|   2. Teardown begins if mid exits the fixed grid envelope.        |
//|   3. If cycleNet reaches target, profit-funded cleanup spends     |
//|      surplus on losing tickets before full target teardown.       |
//|   4. Stale/off-grid Straddle pendings are deleted.                |
//|   5. POPULATION refills empty fixed-level slots.                  |
//|   6. PER-LEG TRAILING manages in-profit legs via broker-side SL.  |
//|   7. Realized-profit cleanup may reduce losers after trailing.    |
//+------------------------------------------------------------------+
void ApplyCompletedActionHandoff(const StrPendingAction &action)
  {
   const StrTradeIntent intent=action.intent;
   const StrActionOutcome outcome=action.outcome;
   g_bookDirty=true;
   if(intent.kind==STR_OP_MARKET_DEAL)
     {
      datetime now=TimeCurrent();
      if(IsRescueHedgeComment(intent.comment))
        {
         g_lastRescueHedgeTime=now;
         PersistRescueHedgeTime();
        }
      else if(IsAveragingComment(intent.comment))
        {
         g_lastBasketAvgEntryTime=now;
         if(outcome.result_price>0.0)
            g_basketAvgLastEntryPrice=outcome.result_price;
         g_lastRescueHedgeTime=now;
         PersistBasketAvgState();
        }
      else if(IsTrendRescueComment(intent.comment))
        {
         g_lastTrendRescueEntryTime=now;
         if(outcome.result_price>0.0)
            g_trendRescueLastEntryPrice=outcome.result_price;
         PersistTrendRescueState();
         InvalidateTrendRescueSnapshot();
         InvalidateTrendRescueBelowMinLotGuards();
        }
     }
   else if(intent.kind==STR_OP_POSITION_REDUCE)
     {
      InvalidateTrendRescueSnapshot();
      InvalidateTrendRescueBelowMinLotGuards();
     }
   Log(2,StringFormat("Straddle: terminal action consumed kind=%d volume=%.2f net=%.2f request=%u",
                      (int)intent.kind,outcome.confirmed_volume,
                      outcome.confirmed_net_money,outcome.request_id));
  }

ulong FindConfirmedDealForPendingAction(const int slot)
  {
   if(slot<0 || slot>=STR_MAX_PENDING_ACTIONS || !g_pendingActions[slot].active)
      return 0;
   datetime now=TimeCurrent();
   if(now<=0 || !HistorySelect(now-86400,now))
      return 0;
   int total=HistoryDealsTotal();
   int work=0;
   for(int i=total-1;i>=0 && work<STR_MAX_TX_WORK;i--,work++)
     {
      ulong deal=HistoryDealGetTicket(i);
      if(deal==0 || HistoryDealGetInteger(deal,DEAL_MAGIC)!=g_pendingActions[slot].intent.magic ||
         HistoryDealGetString(deal,DEAL_SYMBOL)!=g_pendingActions[slot].intent.symbol)
         continue;
      ulong dealOrder=(ulong)HistoryDealGetInteger(deal,DEAL_ORDER);
      ulong dealPosition=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
      if((g_pendingActions[slot].order>0 && dealOrder==g_pendingActions[slot].order) ||
         (g_pendingActions[slot].intent.position_identifier>0 &&
          dealPosition==g_pendingActions[slot].intent.position_identifier))
         return deal;
     }
   return 0;
  }

bool ProcessPendingActionHandoffs()
  {
   bool touched=false;
   bool publishedRemainder=false;
   // Ordinary tick/timer recovery: broker callbacks are advisory, so retry a
   // history row that was not yet selectable and re-probe non-deal operations.
   // This loop is fixed-bounded and remains send-free.
   for(int i=0;i<STR_MAX_PENDING_ACTIONS;i++)
     {
      if(!g_pendingActions[i].active || g_pendingActions[i].terminal_ready)
         continue;
      if(PendingActionLiveCompletionConfirmed(i))
        {
         g_pendingActions[i].outcome.state=COMPLETED;
         g_pendingActions[i].outcome.reason=STR_REASON_NONE;
         g_pendingActions[i].terminal_ready=true;
         g_pendingActions[i].terminal_consumed=false;
         continue;
        }
      ulong retryDeal=g_pendingActions[i].deal;
      if(retryDeal==0)
         retryDeal=FindConfirmedDealForPendingAction(i);
      if(retryDeal>0)
        {
         MqlTradeTransaction retryTrans;
         MqlTradeRequest retryRequest;
         MqlTradeResult retryResult;
         ZeroMemory(retryTrans);
         ZeroMemory(retryRequest);
         ZeroMemory(retryResult);
         retryTrans.type=TRADE_TRANSACTION_HISTORY_ADD;
         retryTrans.deal=retryDeal;
         retryTrans.order=g_pendingActions[i].order;
         retryTrans.position=g_pendingActions[i].intent.position;
         retryResult.request_id=g_pendingActions[i].outcome.request_id;
         retryResult.retcode=g_pendingActions[i].outcome.retcode;
         ReconcileTradeTransaction(retryTrans,retryRequest,retryResult,STR_MAX_TX_WORK);
        }
     }
   for(int i=0;i<STR_MAX_PENDING_ACTIONS;i++)
     {
      if(!g_pendingActions[i].active || !g_pendingActions[i].terminal_ready ||
         g_pendingActions[i].terminal_consumed)
         continue;
      touched=true;
      g_pendingActions[i].terminal_consumed=true;
      g_pendingActions[i].terminal_ready=false;
      if(g_pendingActions[i].outcome.state==COMPLETED)
        {
         StrPendingAction completed=g_pendingActions[i];
         ApplyCompletedActionHandoff(completed);
         ReleasePendingActionReservation(i);
        }
      else if(g_pendingActions[i].outcome.state==PARTIAL &&
              g_pendingActions[i].terminal_short_fill &&
              g_pendingActions[i].outcome.remaining_volume>
                 VolumeTolerance(g_pendingActions[i].intent.symbol))
        {
         g_pendingActions[i].remainder_authorized=true;
         g_pendingActions[i].updated_at=TimeCurrent();
         publishedRemainder=true;
        }
     }
   // Publication and resend are deliberately separated by one ordinary state
   // machine invocation, making the terminal PARTIAL observable exactly once.
   if(publishedRemainder)
      return true;

   for(int i=0;i<STR_MAX_PENDING_ACTIONS;i++)
     {
      if(!g_pendingActions[i].active)
         continue;
      touched=true;
      if(!g_pendingActions[i].remainder_authorized)
         continue;
      StrTradeIntent remainderIntent=g_pendingActions[i].intent;
      remainderIntent.requested_volume=NormalizeVolumeDown(remainderIntent.symbol,
         g_pendingActions[i].outcome.remaining_volume);
      if(remainderIntent.requested_volume<=VolumeTolerance(remainderIntent.symbol))
        {
         ReleasePendingActionReservation(i);
         return true;
        }
      StrActionOutcome remainderOutcome=CheckAndSendRequest(remainderIntent);
      if(remainderOutcome.state==COMPLETED)
        {
         StrPendingAction completed;
         ZeroMemory(completed);
         completed.intent=remainderIntent;
         completed.outcome=remainderOutcome;
         ApplyCompletedActionHandoff(completed);
        }
      return true;
     }
   return touched;
  }

void ManageCycle()
  {
   // Reconcile/consume exact action handoffs first, but never globally block
   // unrelated teardown, emergency reduction, trailing, or cleanup work.
   // A matching intent remains blocked by its own live reservation.
   ProcessPendingActionHandoffs();

    // 1.1.24: invalidate the aggregate before the strict open-book check so
    // floating P/L is fresh for this tick/timer pass.
    g_bookDirty = true;

    // 1.1.18: daily profit/loss limits (blocks new cycle/grid when hit)
    UpdateDailyTradingLimits();

   // 1.1.8 FAST EQUITY RECOVERY — run early: stop risk + cleanup + float.
   // Does not replace teardown; runs before normal populate/start.
   if(!g_tearingDown && InpFastEquityRecovery && EquityRecoveryActive())
     {
      ProcessFastEquityRecovery();
      // If trend rescue was started, process it this tick
      if(IsTrendRescueActive())
        {
         double sb=0.0, sa=0.0;
         if(RefreshCurrentPrices(sb, sa) && sb > 0.0 && sa > 0.0)
            ProcessTrendRescue(sb, sa);
        }
      // Continue into rest of ManageCycle for float green / STATUS, but
      // populate/start are already gated by EquityRecoveryActive().
     }
   // 0.0.45: optional decision-throttle. Default OFF (==0) => this block is skipped
   // entirely => byte-identical to 0.0.44. When >0, skip this tick's decision pass
   // if NO position/order opened or closed since last pass AND we are still within
   // the throttle window. Fills/closes (count change) always force an immediate pass,
   // so broker-side fill accuracy is preserved; only redundant sub-second decision
   // re-evaluations are skipped.
   if(InpManageThrottleSeconds > 0 && !IsStopped())
     {
      int mtCount = PositionsTotal() + OrdersTotal();
      if(mtCount == g_lastBookCountSeen &&
         (long)(TimeCurrent() - g_lastManageProcess) < (long)InpManageThrottleSeconds)
         return;
      g_lastManageProcess = TimeCurrent();
      g_lastBookCountSeen = mtCount;
     }

   // 0.0.40 O1: a new tick (OnTick OR OnTimer both route here) means price and
   // time advanced, so the broker-live floating/PROFIT/SWAP members and the
   // stale-age membership of the aggregate cache are stale. Mark dirty so the
   // first cached read this tick re-walks the book once with fresh prices.
   // This is the mandatory tick-start invalidation; the per-trade-op sets cover
   // intra-tick book mutations.
   g_bookDirty = true;

   if(IsStopped())
      return;

   // 0.0.37 (C5): keep the peak-equity high-water mark current on every tick of
   // either OnTick or OnTimer, regardless of cycle state, so the persisted peak
   // reflects the true high water even when the normal-cycle hedge tail is not
   // reached (teardown / rescue / flat). No-op cost when peak has not grown.
   UpdatePeakEquity();

   // 1.1.6: STATUS only at logLevel>=1, at most every 10 minutes (was 120s and
   // flooded multi-GB agent logs during sleeper stuck states).
   if(EffectiveLogLevel() >= 1 &&
      (g_lastStatusHeartbeat == 0 || (TimeCurrent() - g_lastStatusHeartbeat) >= 600))
     {
      const int statusPos = CountMyPositions();
      if(statusPos > 0 || g_floatCount > 0 || g_tearingDown || g_trendRescueActive || g_rescueHolding)
        {
         double statusOpenLots = 0.0;
         const double statusFloating = OpenFloatingPL(statusOpenLots);
         LogState(1, "status-hb",
                  StringFormat("Straddle: STATUS f=%d pos=%d pend=%d net=%.0f fl=%.0f eq=%.0f tear=%d tr=%d",
                               g_floatCount, statusPos, CountMyPendings(),
                               CycleNet(), statusFloating,
                               AccountInfoDouble(ACCOUNT_EQUITY),
                               (g_tearingDown ? 1 : 0),
                               (g_trendRescueActive ? 1 : 0)),
                  600);
        }
      g_lastStatusHeartbeat = TimeCurrent();
     }


   // Float-only state-agnostic trigger foundation. All time thresholds consume
   // the checked OnInit caches; no retired setting can activate this path.
   if(InpUseFloatReanchor)
     {
      const datetime floatNow=TimeCurrent();
      const int floatCyclePositions=CountMyPositions()-CountFloatPositions();
      if(floatCyclePositions>0)
        {
         const double floatCycleNet=CycleNet();
         const double floatTarget=MoneyInput(InpTargetUSD);
         const double floatStaleAge=(g_floatStaleSeconds>0 ? OldestCyclePositionAgeSeconds() : 0.0);
         const bool floatCooldownOk=(g_lastStaleRelease==0 ||
                                     (floatNow>=g_lastStaleRelease &&
                                      (g_floatStaleCooldownSeconds==0 ||
                                       floatNow-g_lastStaleRelease>=g_floatStaleCooldownSeconds)));
         const bool floatStale=(g_floatStaleSeconds>0 &&
                                floatStaleAge>=(double)g_floatStaleSeconds &&
                                floatCycleNet<floatTarget &&
                                MathAbs(floatCycleNet)<=g_floatStaleMaxNetMoney &&
                                floatCooldownOk);
         bool floatRed=false;
         double floatBid=0.0,floatAsk=0.0;
         if(RefreshCurrentPrices(floatBid,floatAsk) && floatBid>0.0 && floatAsk>0.0)
           {
            double floatLots=0.0;
            const double floatCycleFloating=CycleFloatingPL(floatLots);
            const double floatThreshold=MathMax(MoneyInput(InpFloatReanchorDDUSD),
                                                MoneyInput(InpCleanupCostBufferUSD));
            const double floatMid=(floatBid+floatAsk)*0.5;
            const bool floatStuck=(g_tearingDown || IsRescueHoldActive() ||
                                   floatMid<GridMinPrice() || floatMid>GridMaxPrice() ||
                                   g_anchor<=0.0);
            const bool floatDwell=(g_cycleStartTrusted && g_cycleStart>0 &&
                                   floatNow>=g_cycleStart &&
                                   (g_floatReanchorSeconds==0 ||
                                    floatNow-g_cycleStart>=g_floatReanchorSeconds));
            floatRed=(floatDwell && floatStuck && (-floatCycleFloating)>=floatThreshold);
           }
         if(floatStale || floatRed)
           {
            const string floatReason=(floatStale ? "Float stale re-anchor" : "Float red structural re-anchor");
            const bool floated=FloatReanchor(floatReason);
            if(floated && floatStale)
               g_lastStaleRelease=floatNow;
            if(floated)
               return;
           }
        }
     }

   if(g_pauseUntil > 0 && TimeCurrent() < g_pauseUntil)
      return;                                           // post-cycle pause
   g_pauseUntil = 0;

   // persistent teardown: a close decision was already taken - drive the
   // cycle to TRULY FLAT before any other state handling. Respect the
   // back-off (closed market OR stuck teardown) so a blocked close-out is
   // re-probed at most once per InpRetrySeconds, not on every tick.
   if(g_tearingDown)
     {
      if(g_nextTearTry > 0 && TimeCurrent() < g_nextTearTry)
         return;
      g_nextTearTry = 0;
      ProcessTearDown();
      return;
     }

   // 0.0.38: account-level basket take-profit. Runs AFTER the g_tearingDown
   // early-return (so it never fights an in-progress teardown) and BEFORE the
   // per-cycle / trend-rescue / rescue-hold branches (so it is fully state-
   // agnostic and can bank a recovered book mid-rescue). If it fires, it runs
   // BeginTearDown -> ProcessTearDown synchronously and sets g_tearingDown;
   // the explicit return below then keeps the rest of ManageCycle from running
   // against an already-closing book this tick.
   CheckBasketTakeProfit();
   if(g_tearingDown)
      return;

   // 0.0.41 FIX A: STATE-AGNOSTIC equity-DD backstop. Runs AFTER the teardown
   // (g_tearingDown) and basket-TP early-returns above - so it never hedges into
   // an in-progress net-close - but BEFORE the rescue-hold / trend-rescue /
   // normal-cycle branches below, so the equity hard-flatten fires REGARDLESS of
   // state (in 0.0.40 it lived in the normal-cycle tail only, so it never ran
   // during rescue-hold or trend-rescue and equity DD overshot the threshold
   // uncapped there). No-op when InpEquityHardFlattenDDUSD<=0 (the backstop self-
   // gates on its second line), so a DEFAULT-config run is unchanged. ManageCycle
   // has not computed bid/ask yet at this point, so fetch fresh prices via the
   // established RefreshCurrentPrices helper; if unavailable, skip this tick
   // (EquityHedgeBackstop/OpenNetHedge also re-check bid/ask>0). The shared
   // g_lastRescueHedgeTime stamp + the floored backstop cooldown + the Count
   // RescueHedges cap make this and the later rescue-state TryOpenRescueHedge
   // mutually exclusive within a tick (no double-open). It OPENS a hedge, never
   // closes, so it NEVER realizes a loss. The old redundant tail call (0.0.40
   // line ~11830) is REMOVED, so there is exactly ONE backstop call per tick in
   // every state.
   {
      double bsBid = 0.0, bsAsk = 0.0;
      if(RefreshCurrentPrices(bsBid, bsAsk) && bsBid > 0.0 && bsAsk > 0.0)
         EquityHedgeBackstop(bsBid, bsAsk);
   }

   int positions = CountMyPositions();
   int pendings  = CountMyPendings();
   int cyclePositions=positions-CountFloatPositions();
   int cyclePendings=pendings;

   // 0.0.44: FLOAT GREEN-CLOSE. Bank any FLOATED leg that has turned GREEN, per
   // ticket, WITHOUT touching the fresh cycle. Mirrors the orphan green-close: runs
   // after the backstop, before the gate. CloseOneFloatTicket realizes ONLY a
   // positive (legFloating >= InpFloatCloseBufferUSD), so it can NEVER create a
   // deferred loss (the no-realize invariant). Hard no-op when the registry is empty
   // (InpUseFloatReanchor=false => g_floatCount==0).
   if(InpUseFloatReanchor && g_floatCount > 0)
     {
      if(FloatGreenClose())
        {
         g_bookDirty = true;
         // refresh Float-derived cycle counts after the close(s)
         positions      = CountMyPositions();
         pendings       = CountMyPendings();
         cyclePositions = positions-CountFloatPositions();
         cyclePendings  = pendings;
        }
     }

   if(IsRescueHoldActive() && cyclePositions == 0)
     {
      ProcessRescueHold(0.0, 0.0);
      return;
     }

   if(IsTrendRescueActive() && cyclePositions == 0)
     {
      ProcessTrendRescue(0.0, 0.0);
      return;
     }

   // 1.1.7: ANY red float bag can enter sleeper rescue (not only float-only books).
   if(g_floatCount > 0 && FloatFloatingPL() < -1.0e-6 && !IsTrendRescueActive())
     {
      if(TryEnterFloatSleeperRescue() && IsTrendRescueActive())
        {
         double sb = 0.0, sa = 0.0;
         if(RefreshCurrentPrices(sb, sa) && sb > 0.0 && sa > 0.0)
            ProcessTrendRescue(sb, sa);
         return;
        }
     }

   if(cyclePositions == 0 && cyclePendings == 0)
     {
      // 1.1.6/1.1.7: float-only or blocked bag — rescue or wait, do not spam StartCycle.
      if(positions > 0 && (CountFloatPositions() >= positions || FloatBagBlocksNewGrid()))
        {
         if(TryEnterFloatSleeperRescue())
           {
            if(IsTrendRescueActive())
              {
               double sb = 0.0, sa = 0.0;
               if(RefreshCurrentPrices(sb, sa))
                  ProcessTrendRescue(sb, sa);
              }
            return;
           }
         if(IsTrendRescueActive())
           {
            double sb = 0.0, sa = 0.0;
            if(RefreshCurrentPrices(sb, sa))
               ProcessTrendRescue(sb, sa);
            return;
           }
         if(FloatBagBlocksNewGrid())
           {
            LogState(1, "float-sleeper-wait",
                     StringFormat("Straddle: ERR sleeper-wait f=%d pl=%.0f eq=%.0f",
                                  g_floatCount, FloatFloatingPL(),
                                  AccountInfoDouble(ACCOUNT_EQUITY)),
                     300);
            return;
           }
        }

      // back-off window after a failed/0-order start or an untradeable market
      if(g_nextBuildTry > 0 && TimeCurrent() < g_nextBuildTry)
         return;
      g_nextBuildTry = 0;

      // tradeability gate: never attempt orders into a closed/disabled market
      if(!CanTrade())
        {
         if(g_wasTradeable)
            Log(1, StringFormat("Straddle: market closed or trading unavailable - cycle start paused, rechecking every %d s",
                                InpRetrySeconds));
         g_wasTradeable = false;
         g_nextBuildTry = TimeCurrent() + InpRetrySeconds;   // throttle re-checks (tester-safe, no Sleep)
         return;
        }
      if(!g_wasTradeable)
        {
         g_wasTradeable = true;
         Log(1, "Straddle: trading available again - resuming cycle starts");
        }

      // 0.0.46: trend-pause filter - do NOT open a new straddle while a strong directional move is
      // detected (would create counter-trend losers); stay flat and re-check next tick.
      // When InpUseTrendPause=false (default), IsTrendPauseActive() returns false immediately
      // => StartCycle() fires exactly as in 0.0.45 (byte-identical).
      if(IsTrendPauseActive())
        {
         return;
        }

      StartCycle();                                     // flat -> new cycle (anchor + full grid)
      return;
     }

   // ---- ACTIVE CYCLE ------------------------------------------------
   double ask = SymbolInfoDouble(g_sym, SYMBOL_ASK);
   double bid = SymbolInfoDouble(g_sym, SYMBOL_BID);
   if(ask <= 0.0 || bid <= 0.0)
      return;                                           // market data not ready - defer

   if(IsTrendRescueActive())
     {
      ProcessTrendRescue(bid, ask);
      return;
     }

   if(IsRescueHoldActive())
     {
      ProcessRescueHold(bid, ask);
      return;
     }

   // restart edge: cycle exists but no trusted anchor was recovered. Never
   // re-baseline an active cycle to the current mid: that can preserve/refill
   // a polluted grid. Tear down persistently and rebuild only after flat.
   if(g_anchor <= 0.0)
     {
      double recoveryNet = CycleNet();
      double closeBuffer = MoneyInput(InpCleanupCostBufferUSD);
      // 0.0.42 REACHABILITY FIX (PART 1): the in-branch no-anchor release trigger
      // was REMOVED here. It was unreachable once the book parked in rescue-hold
      // (the L~12328 gate returned first). The single state-agnostic release check
      // earlier in ManageCycle (before the rescue-hold/trend-rescue gates) now
      // supersedes it; this branch is restored to its original 0.0.41 fall-through.
      if(cyclePositions > 0 && recoveryNet < closeBuffer)
        {
         EnterRescueHold(StringFormat("recovery rescue hold - no trusted anchor, cycleNet %.2f below close buffer %.2f, positions %d, pendings %d, deleting pendings only, holding positions; balance protected but equity risk remains",
                                      recoveryNet, closeBuffer, positions, pendings));
        }
      else
        {
         BeginTearDown(StringFormat("no trusted recovery anchor for active cycle, positions %d, pendings %d - refusing refill until flat",
                                    positions, pendings),
                       closeBuffer);
        }
      return;
     }

   // 1. FULL CLOSE paths - strict grid-boundary exit or net target.
   // 1.1.14: after TREND-2X arms this cycle, bank at FastBankUSD for quick cleanup.
   double net = CycleNet();
   double closeBuffer = MoneyInput(InpCleanupCostBufferUSD);
   double target = EffectiveCycleTargetUSD();
   double mid = (bid + ask) / 2.0;
   double gridMin = GridMinPrice();
   double gridMax = GridMaxPrice();
   if(mid < gridMin || mid > gridMax)
     {
      if(cyclePositions > 0 && net < closeBuffer)
        {
         // 0.0.42 REACHABILITY FIX (PART 1): the in-branch boundary release trigger
         // was REMOVED here. Same unreachability class as the no-anchor trigger -
         // the rescue-hold gate (L~12328) returned before this branch ran once the
         // book was parked. The single state-agnostic release check earlier in
         // ManageCycle now supersedes it; this branch is restored to its original
         // 0.0.41 fall-through (EnterTrendRescue / EnterRescueHold).
         if(InpUseTrendRescueMode)
           {
            int direction = (mid > gridMax ? 1 : -1);
            EnterTrendRescue(direction, mid, gridMin, gridMax, net, positions, pendings);
           }
         else
           {
            EnterRescueHold(StringFormat("boundary rescue hold - cycleNet %.2f below close buffer %.2f, mid %s, anchor %s, gridMin %s, gridMax %s, positions %d, pendings %d, deleting pendings only, holding positions; balance protected but equity risk remains",
                                         net, closeBuffer,
                                         DoubleToString(mid, g_digits),
                                         DoubleToString(g_anchor, g_digits),
                                         DoubleToString(gridMin, g_digits),
                                         DoubleToString(gridMax, g_digits),
                                         positions, pendings));
           }
        }
      else
        {
         BeginTearDown(StringFormat("grid-boundary safe exit, mid %s, anchor %s, gridMin %s, gridMax %s, positions %d, pendings %d, cycleNet %.2f",
                                    DoubleToString(mid, g_digits),
                                    DoubleToString(g_anchor, g_digits),
                                    DoubleToString(gridMin, g_digits),
                                    DoubleToString(gridMax, g_digits),
                                    positions, pendings, net),
                       closeBuffer);
        }
      return;
     }
   if(net >= target)
     {
      // 0.0.37 (C2): the on-target leftover-cleanup nibble is kept (a single
      // profit-funded pass on the worst losers is fine here), but the old
      // 'delaying full teardown' early-return that DELAYED the guaranteed
      // teardown is REMOVED. That early-return converted a guaranteed +target
      // bank into voluntarily-realized small losses plus a still-open loser pile
      // and lengthened cycle time. Reaching target now ALWAYS banks; leftover
      // losers are addressed by the dedicated hedge/rescue/stale lanes and the
      // section-3 leftover path, not by the bank trigger. The losingAfterCleanup
      // / cleanupBudgetAfter locals fed only the removed gate and are dropped.
      TryProfitFundedCleanup("profit-first");
      net = CycleNet();
      if(net >= target)
        {
         const string bankWhy = (g_trend2xArmedThisCycle &&
                                 target + 1.0e-8 < MoneyInput(InpTargetUSD) + 1.0e-8)
                                ? "TREND-2X fast bank"
                                : "net target reached";
         BeginTearDown(StringFormat("%s, cycleNet %.2f >= %.2f", bankWhy, net, target),
                       target);
         return;
        }
     }

   // 2. POPULATION - refill every empty (side, level) slot at its FIXED
   //    price. Back-off only after a pass that hit a hard limit or a
   //    market-closed retcode (bounds retry/log storms); a normal pass on
   //    a fully-occupied grid sends no requests and runs every tick.
   if(g_nextBuildTry == 0 || TimeCurrent() >= g_nextBuildTry)
     {
      g_nextBuildTry = 0;
      if(IsStopped())
         return;
      CleanupOffGridPendings();
      if(IsStopped())
         return;
      // 1.1.1: drop counter-trend grid pendings while one-side bias is active,
      // then refill only with-trend slots via PopulateGrid (before place).
      CancelCounterTrendGridPendings();
      if(IsStopped())
         return;
      if(CanTrade())
         PopulateGrid();
      if(g_abortBuild || g_buildSkipWait)
         g_nextBuildTry = TimeCurrent() + InpRetrySeconds;
     }

   // 2b. 1.1.10 BASKET BE RECOVERY — after grid populate, before/around trail:
   //     price rose & buys open => add SELL-stops under market until float~0.
   if(positions > 0 && CanTrade())
      ProcessBasketBreakEvenRecovery();

   // 3. PER-LEG TRAILING - bank in-profit legs (1.1.15 price-grid trail)
   // No forced recovery cleanup here — NeverRealizeLoss keeps balance from dipping.
   if(positions > 0)
     {
      UpdateLegTracking(bid, ask);
      ManagePerLegTrailing(bid, ask);
      double afterTrailingNet = CycleNet();
      if(afterTrailingNet < 0.0)
         TryProfitFundedCleanup("loss-cleanup-first");
      else
         TryProfitFundedCleanup("profit-first");
     }

   // 4. 0.0.35: STATE-AGNOSTIC NET-EXPOSURE HEDGE. Reachable in the NORMAL CYCLE
   //    so an orphan/stale net-long (or net-short) loser pile carried outside
   //    cycle accounting gets hedged and equity drawdown stops growing. Both
   //    callees self-gate (InpUseRescueHedge / account-hedging / ACCOUNT_HEDGE_
   //    ALLOWED / cooldown / max-hedges / loss-trigger / net-lots / margin), so
   //    nothing opens unless conditions hold. Harvest BEFORE open, mirroring
   //    ProcessRescueHold ordering. This tail is reached ONLY in a normal active
   //    cycle: teardown (g_tearingDown), rescue-hold and trend-rescue all
   //    early-return from ManageCycle above, and the net>=target / boundary
   //    branches all 'return' before here, so the open is called from exactly
   //    one of {ProcessRescueHold, ProcessTrendRescue, this tail} per tick - no
   //    double-open. The InpRescueHedgeCooldownSec guard further prevents any
   //    double-open across the OnTick/OnTimer ManageCycle invocations.
   if(positions > 0 && bid > 0.0 && ask > 0.0)
     {
      // 3b. 0.0.39: BOUNDED AVERAGING-DOWN. Called FIRST so that, on a
      //     successful add, it stamps the SHARED g_lastRescueHedgeTime
      //     one-open-per-tick stamp; the rescue hedge and equity backstop
      //     below then see that stamp on their cooldown check and decline to
      //     open a SECOND market order this tick (no opposing same-tick
      //     orders, no double-stack margin spike). The engine ALSO hard-gates
      //     itself against the entire hedge/rescue/backstop family, so the
      //     two paths can never both fire on the same tick. Self-gates on
      //     InpUseBasketAveraging (default OFF = bit-for-bit 0.0.38).
      if(CanTrade())
         TryBasketAveragingEntry(bid, ask);
      TryHarvestRescueHedges();
      if(CanTrade())
         TryOpenRescueHedge(bid, ask);
      // 0.0.41 FIX A: the equity-DD circuit breaker (EquityHedgeBackstop) is no
      //    longer called here. It now runs ONCE at the top of ManageCycle (after
      //    the teardown / basket-TP early-returns, before the rescue-hold /
      //    trend-rescue / normal-cycle branches) so it fires state-agnostically;
      //    leaving the old tail call would double-invoke it per normal-cycle tick
      //    (redundant arm/release log + DD recompute; the shared g_lastRescue
      //    HedgeTime stamp already prevents a second open). One call per tick in
      //    every state.
     }
  }

//+------------------------------------------------------------------+
//| Recover cycle state purely from existing positions + pendings     |
//| (magic+symbol) so a restart/recompile resumes cleanly.            |
//| 0.0.10: pending-order slot prices are authoritative for anchor     |
//| recovery. Position prices are only a wider, slippage-aware fallback|
//| when no parsable EA pendings exist.                                |
//| If no trusted anchor can be proven while positions/pendings exist, |
//| persistent teardown starts and no refill is allowed until flat.    |
//| Per-leg peaks are re-seeded at the ENTRY price: favorable          |
//| excursion seen before the restart is forgotten, so the trail       |
//| re-ratchets from entry (closes remain profit-gated).               |
//+------------------------------------------------------------------+
void RecoverCycleState()
  {
   datetime earliest = 0;
   int      positions = 0, pendings = 0;
   bool     hadTrustedCycleStart = (g_cycleStartTrusted && g_cycleStart > 0);

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      // Registered Float positions belong to an earlier isolated cycle.
      if(IsFloatTicket(ticket))
         continue;
      positions++;
      datetime t = (datetime)PositionGetInteger(POSITION_TIME);
      if(earliest == 0 || t < earliest)
         earliest = t;
     }
   // count pendings ALWAYS (also when positions exist) so the recovery log
   // reports the true pending count; their setup time is only used as the
   // cycle-start fallback when no positions exist
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;
      pendings++;
      datetime t = (datetime)OrderGetInteger(ORDER_TIME_SETUP);
      if(positions == 0 && (earliest == 0 || t < earliest))
         earliest = t;
     }
   if(positions == 0 && pendings == 0)
     {
      ResetCycleState();
      return;                                           // flat - nothing to recover
     }

   if(hadTrustedCycleStart && (earliest == 0 || g_cycleStart <= earliest))
     {
      Log(1, StringFormat("Straddle: recovered active cycle (%d positions, %d pendings), exact cycle start %s",
                          positions, pendings, TimeToString(g_cycleStart, TIME_DATE | TIME_SECONDS)));
     }
   else
     {
      if(hadTrustedCycleStart && earliest > 0 && g_cycleStart > earliest)
         Log(0, StringFormat("Straddle: WARN persisted cycle start %s is after earliest live ticket %s - treating cycle P/L window as untrusted",
                             TimeToString(g_cycleStart, TIME_DATE | TIME_SECONDS),
                             TimeToString(earliest, TIME_DATE | TIME_SECONDS)));

      g_cycleStart = earliest;
      g_cycleStartTrusted = false;
      if(earliest > 0)
         Log(1, StringFormat("Straddle: recovered active cycle (%d positions, %d pendings), reconstructed UNTRUSTED cycle start %s",
                             positions, pendings, TimeToString(earliest, TIME_DATE | TIME_SECONDS)));
      else
         Log(1, StringFormat("Straddle: recovered active cycle (%d positions, %d pendings), no trusted cycle start marker available",
                             positions, pendings));
     }

   if(IsTrendRescueActive())
     {
      ArrayResize(g_legs, 0);
      for(int i = PositionsTotal() - 1; i >= 0; i--)
        {
         ulong ticket = PositionGetTicket(i);
         if(ticket == 0)
            continue;
         if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
            continue;
         if(PositionGetString(POSITION_SYMBOL) != g_sym)
            continue;
         int sz = ArraySize(g_legs);
         ArrayResize(g_legs, sz + 1);
         g_legs[sz].ticket = ticket;
         g_legs[sz].peak   = PositionGetDouble(POSITION_PRICE_OPEN);
        }
      Log(1, StringFormat("Straddle: trend rescue restart skips normal grid anchor recovery/refill; direction %s, positions %d, pendings %d",
                          TrendRescueDirectionName(), positions, pendings));
      return;
     }

   EnsureCycleGridStepDistanceForRecovery(positions, pendings);

   // --- re-derive the FIXED anchor from source-separated candidates ---
   // Pending prices should equal fixed grid slot prices. Filled positions
   // can include normal execution slippage, so they must not be mixed with
   // pending prices under the strict one-tick consensus rule.
   double pendingAnchorCandidates[];
   int    pendingCandidateCount = 0;
   double positionAnchorCandidates[];
   int    positionCandidateCount = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)
         continue;
      if(OrderGetString(ORDER_SYMBOL) != g_sym)
         continue;
      bool isBuy; int lvl;
      if(!ParseLegComment(OrderGetString(ORDER_COMMENT), isBuy, lvl))
         continue;
      double p = OrderGetDouble(ORDER_PRICE_OPEN);      // = fixed level price (unless clamped)
      double candidate = SnapPrice(isBuy ? p - lvl * CycleGridStepDistance() : p + lvl * CycleGridStepDistance());
      ArrayResize(pendingAnchorCandidates, pendingCandidateCount + 1);
      pendingAnchorCandidates[pendingCandidateCount] = candidate;
      pendingCandidateCount++;
     }
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      if(IsFloatTicket(ticket))
         continue;                       // 0.0.44: a floated leg's old price must not drag the re-derived anchor
      bool isBuy; int lvl;
      if(!ParseLegComment(PositionGetString(POSITION_COMMENT), isBuy, lvl))
         continue;
      double p = PositionGetDouble(POSITION_PRICE_OPEN);   // ~ level price + entry slippage
      double candidate = SnapPrice(isBuy ? p - lvl * CycleGridStepDistance() : p + lvl * CycleGridStepDistance());
      ArrayResize(positionAnchorCandidates, positionCandidateCount + 1);
      positionAnchorCandidates[positionCandidateCount] = candidate;
      positionCandidateCount++;
     }

   if(pendingCandidateCount > 0)
     {
      double pendingCandidateMin = pendingAnchorCandidates[0];
      double pendingCandidateMax = pendingAnchorCandidates[0];
      double pendingCandidateSum = 0.0;
      for(int i = 0; i < pendingCandidateCount; i++)
        {
         pendingCandidateSum += pendingAnchorCandidates[i];
         if(pendingAnchorCandidates[i] < pendingCandidateMin)
            pendingCandidateMin = pendingAnchorCandidates[i];
         if(pendingAnchorCandidates[i] > pendingCandidateMax)
            pendingCandidateMax = pendingAnchorCandidates[i];
        }

      const double pendingTolerance = TickSize();
      const double pendingCandidateRange = pendingCandidateMax - pendingCandidateMin;
      if(pendingCandidateRange > pendingTolerance)
        {
         g_anchor = 0.0;
         // A dirty/inconsistent legacy grid can enter persistent teardown
         // immediately on init; this is intentional to prevent refilling a
         // polluted grid with an untrusted anchor.
         BeginTearDownOrRescue(StringFormat("recovery anchor inconsistency: pending consensus failed, %d pending candidates range %s..%s, diff %s > tolerance %s, positions %d, pendings %d - dirty/inconsistent legacy grid, refusing refill until flat",
                                            pendingCandidateCount,
                                            DoubleToString(pendingCandidateMin, g_digits),
                                            DoubleToString(pendingCandidateMax, g_digits),
                                            DoubleToString(pendingCandidateRange, g_digits),
                                            DoubleToString(pendingTolerance, g_digits),
                                            positions, pendings));
         return;
        }

      g_anchor = SnapPrice(pendingCandidateSum / pendingCandidateCount);
      Log(1, StringFormat("Straddle: trusted anchor %s recovered from pending consensus (%d pending candidates, %d position candidates ignored, range %s..%s, tolerance %s)",
                          DoubleToString(g_anchor, g_digits),
                          pendingCandidateCount,
                          positionCandidateCount,
                          DoubleToString(pendingCandidateMin, g_digits),
                          DoubleToString(pendingCandidateMax, g_digits),
                          DoubleToString(pendingTolerance, g_digits)));
     }
   else if(positionCandidateCount > 0)
     {
      double positionCandidateMin = positionAnchorCandidates[0];
      double positionCandidateMax = positionAnchorCandidates[0];
      double positionCandidateSum = 0.0;
      for(int i = 0; i < positionCandidateCount; i++)
        {
         positionCandidateSum += positionAnchorCandidates[i];
         if(positionAnchorCandidates[i] < positionCandidateMin)
            positionCandidateMin = positionAnchorCandidates[i];
         if(positionAnchorCandidates[i] > positionCandidateMax)
            positionCandidateMax = positionAnchorCandidates[i];
        }

      double ask = SymbolInfoDouble(g_sym, SYMBOL_ASK);
      double bid = SymbolInfoDouble(g_sym, SYMBOL_BID);
      double spread = ((ask > 0.0 && bid > 0.0 && ask >= bid) ? ask - bid : 0.0);
      double positionTolerance = MathMax(TickSize(),
                                         MathMin(CycleGridStepDistance() * 0.25,
                                                 MathMax(spread * 2.0, TickSize() * 3.0)));
      double positionCandidateRange = positionCandidateMax - positionCandidateMin;
      if(positionCandidateRange > positionTolerance)
        {
         g_anchor = 0.0;
         // A dirty/inconsistent legacy grid can enter persistent teardown
         // immediately on init; this is intentional to prevent refilling a
         // polluted grid with an untrusted anchor.
         BeginTearDownOrRescue(StringFormat("recovery anchor inconsistency: position-only fallback failed, %d position candidates range %s..%s, diff %s > tolerance %s (spread %s), positions %d, pendings %d - dirty/inconsistent legacy grid, refusing refill until flat",
                                            positionCandidateCount,
                                            DoubleToString(positionCandidateMin, g_digits),
                                            DoubleToString(positionCandidateMax, g_digits),
                                            DoubleToString(positionCandidateRange, g_digits),
                                            DoubleToString(positionTolerance, g_digits),
                                            DoubleToString(spread, g_digits),
                                            positions, pendings));
         return;
        }

      g_anchor = SnapPrice(positionCandidateSum / positionCandidateCount);
      Log(1, StringFormat("Straddle: position-only approximate anchor %s recovered from %d position candidates (range %s..%s, tolerance %s, spread %s)",
                          DoubleToString(g_anchor, g_digits),
                          positionCandidateCount,
                          DoubleToString(positionCandidateMin, g_digits),
                          DoubleToString(positionCandidateMax, g_digits),
                          DoubleToString(positionTolerance, g_digits),
                          DoubleToString(spread, g_digits)));
     }
   else
     {
      g_anchor = 0.0;
      // A dirty/inconsistent legacy grid can enter persistent teardown
      // immediately on init; this is intentional to prevent refilling a
      // polluted grid with an untrusted anchor.
      BeginTearDownOrRescue(StringFormat("recovery anchor inconsistency: 0 parsable pending or position candidates, positions %d, pendings %d - dirty/inconsistent legacy grid, refusing refill until flat",
                                         positions, pendings));
      return;
     }

   // --- re-seed per-leg tracking: peak = entry price (approximation) -
   //     favorable excursion before the restart is forgotten; the trail
   //     re-ratchets from entry and closes remain profit-gated
   ArrayResize(g_legs, 0);
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if(PositionGetString(POSITION_SYMBOL) != g_sym)
         continue;
      int sz = ArraySize(g_legs);
      ArrayResize(g_legs, sz + 1);
      g_legs[sz].ticket = ticket;
      g_legs[sz].peak   = PositionGetDouble(POSITION_PRICE_OPEN);
     }

   if(positions > 0 && !g_cycleStartTrusted && !IsTrendRescueActive())
      EnterRescueHold(StringFormat("recovery rescue hold - cycle start marker missing/untrusted, positions %d, pendings %d, deleting pendings only, holding positions; balance protected but equity risk remains",
                                   positions, pendings));
  }

//+------------------------------------------------------------------+
//| Straddle chart UI helpers (visual-only, no trading side effects) |
//+------------------------------------------------------------------+
datetime StrUiClockNow()
  {
   datetime now = TimeLocal();
   if(now <= 0)
      now = TimeCurrent();
   return now;
  }

int StrUiClampInt(const int value, const int minValue, const int maxValue)
  {
   int clamped = value;
   if(clamped < minValue)
      clamped = minValue;
   if(clamped > maxValue)
      clamped = maxValue;
   return clamped;
  }

bool StrUiIsNonVisualTester()
  {
   return (MQLInfoInteger(MQL_TESTER) != 0 && MQLInfoInteger(MQL_VISUAL_MODE) == 0);
  }

bool StrUiIsVisualTester()
  {
   return (MQLInfoInteger(MQL_TESTER) != 0 && MQLInfoInteger(MQL_VISUAL_MODE) != 0);
  }

bool StrUiEnabled()
  {
   return (InpShowStraddleChartUI && !StrUiIsNonVisualTester());
  }

int StrUiDashboardRefreshSeconds()
  {
   int seconds = StrUiClampInt(InpStraddleUIDashboardRefreshSeconds, 1, 60);
   if(StrUiIsVisualTester() && seconds < 5)
      seconds = 5;
   return seconds;
  }

int StrUiHistoryRefreshSeconds()
  {
   return (StrUiIsVisualTester() ? 60 : 15);
  }

int StrUiLifetimeHistoryRefreshSeconds()
  {
   return 300;
  }

int StrUiMarkerRefreshSeconds()
  {
   return (StrUiIsVisualTester() ? 60 : 20);
  }

int StrUiMarkerLookbackDays()
  {
   return StrUiClampInt(InpStraddleProfitMarkerLookbackDays, 1, 365);
  }

int StrUiMaxProfitMarkers()
  {
   return StrUiClampInt(InpStraddleMaxProfitMarkers, 0, 100);
  }

int StrUiDashboardWidth()
  {
   int width = 268;
   long chartWidth = ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0);
   if(chartWidth > 0 && chartWidth < 310)
     {
      width = (int)chartWidth - 24;
      if(width < 196)
         width = 196;
     }
   return width;
  }

datetime StrUiDayStart(const datetime value)
  {
   MqlDateTime parts;
   TimeToStruct(value, parts);
   parts.hour = 0;
   parts.min = 0;
   parts.sec = 0;
   return StructToTime(parts);
  }

datetime StrUiWeekStart(const datetime value)
  {
   datetime dayStart = StrUiDayStart(value);
   MqlDateTime parts;
   TimeToStruct(dayStart, parts);
   int daysFromMonday = (parts.day_of_week == 0 ? 6 : parts.day_of_week - 1);
   return (datetime)(dayStart - (long)daysFromMonday * 86400);
  }

string StrUiMoneyText(const double accountMoney, const bool signedValue)
  {
   double displayMoney = AccountMoneyToDisplay(accountMoney);
   string sign = "";
   if(displayMoney < 0.0)
      sign = "-";
   else if(signedValue && displayMoney > 0.0)
      sign = "+";
   return sign + "$" + DoubleToString(MathAbs(displayMoney), 2);
  }

double StrUiDealNet(const ulong deal)
  {
   return HistoryDealGetDouble(deal, DEAL_PROFIT) +
          HistoryDealGetDouble(deal, DEAL_SWAP) +
          HistoryDealGetDouble(deal, DEAL_COMMISSION) +
          HistoryDealGetDouble(deal, DEAL_FEE);
  }

bool StrUiIsOwnCloseDeal(const ulong deal)
  {
   if(deal == 0)
      return false;
   if(HistoryDealGetString(deal, DEAL_SYMBOL) != g_sym)
      return false;
   if((long)HistoryDealGetInteger(deal, DEAL_MAGIC) != InpMagic)
      return false;
   ENUM_DEAL_ENTRY entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal, DEAL_ENTRY);
   return (entry == DEAL_ENTRY_OUT || entry == DEAL_ENTRY_INOUT || entry == DEAL_ENTRY_OUT_BY);
  }

void StrUiDeleteObjectsByPrefix(const string prefix)
  {
   for(int i = ObjectsTotal(0, -1, -1) - 1; i >= 0; i--)
     {
      string name = ObjectName(0, i, -1, -1);
      if(StringFind(name, prefix) == 0)
         ObjectDelete(0, name);
     }
  }

void StrUiCaptureOriginalChartSkin()
  {
   if(g_strUiChartSkinApplied)
      return;

   g_strUiOriginalColorBackground = (color)ChartGetInteger(0, CHART_COLOR_BACKGROUND, 0);
   g_strUiOriginalColorForeground = (color)ChartGetInteger(0, CHART_COLOR_FOREGROUND, 0);
   g_strUiOriginalColorGrid = (color)ChartGetInteger(0, CHART_COLOR_GRID, 0);
   g_strUiOriginalColorChartUp = (color)ChartGetInteger(0, CHART_COLOR_CHART_UP, 0);
   g_strUiOriginalColorChartDown = (color)ChartGetInteger(0, CHART_COLOR_CHART_DOWN, 0);
   g_strUiOriginalColorCandleBull = (color)ChartGetInteger(0, CHART_COLOR_CANDLE_BULL, 0);
   g_strUiOriginalColorCandleBear = (color)ChartGetInteger(0, CHART_COLOR_CANDLE_BEAR, 0);
   g_strUiOriginalColorBid = (color)ChartGetInteger(0, CHART_COLOR_BID, 0);
   g_strUiOriginalColorAsk = (color)ChartGetInteger(0, CHART_COLOR_ASK, 0);
   g_strUiOriginalColorLast = (color)ChartGetInteger(0, CHART_COLOR_LAST, 0);
   g_strUiOriginalColorStopLevel = (color)ChartGetInteger(0, CHART_COLOR_STOP_LEVEL, 0);
   g_strUiChartSkinApplied = true;
  }

void StrUiRestoreChartSkin()
  {
   if(!g_strUiChartSkinApplied)
      return;

   ChartSetInteger(0, CHART_COLOR_BACKGROUND, g_strUiOriginalColorBackground);
   ChartSetInteger(0, CHART_COLOR_FOREGROUND, g_strUiOriginalColorForeground);
   ChartSetInteger(0, CHART_COLOR_GRID, g_strUiOriginalColorGrid);
   ChartSetInteger(0, CHART_COLOR_CHART_UP, g_strUiOriginalColorChartUp);
   ChartSetInteger(0, CHART_COLOR_CHART_DOWN, g_strUiOriginalColorChartDown);
   ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, g_strUiOriginalColorCandleBull);
   ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, g_strUiOriginalColorCandleBear);
   ChartSetInteger(0, CHART_COLOR_BID, g_strUiOriginalColorBid);
   ChartSetInteger(0, CHART_COLOR_ASK, g_strUiOriginalColorAsk);
   ChartSetInteger(0, CHART_COLOR_LAST, g_strUiOriginalColorLast);
   ChartSetInteger(0, CHART_COLOR_STOP_LEVEL, g_strUiOriginalColorStopLevel);
   g_strUiChartSkinApplied = false;
  }

void StrUiCleanup()
  {
   StrUiDeleteObjectsByPrefix(STR_UI_PREFIX);
   if(g_strUiBackgroundDynamicReady && g_strUiBackgroundResourcePath != "")
     {
      ResourceFree(g_strUiBackgroundResourcePath);
      g_strUiBackgroundDynamicReady = false;
     }
   g_strUiBackgroundResourceChecked = false;
   g_strUiBackgroundResourceName = "";
   g_strUiBackgroundResourcePath = "";
   StrUiRestoreChartSkin();
  }

bool StrUiEnsureRectangleLabel(const string name,
                               const int corner,
                               const int x,
                               const int y,
                               const int width,
                               const int height,
                               const color background,
                               const color border,
                               const bool back)
  {
   if(ObjectFind(0, name) < 0)
     {
      if(!ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0))
         return false;
     }

   ObjectSetInteger(0, name, OBJPROP_CORNER, corner);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE, width);
   ObjectSetInteger(0, name, OBJPROP_YSIZE, height);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR, background);
   ObjectSetInteger(0, name, OBJPROP_COLOR, border);
   ObjectSetInteger(0, name, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, name, OBJPROP_BACK, back);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, 0);
   return true;
  }

bool StrUiEnsureLabel(const string name,
                      const int corner,
                      const int x,
                      const int y,
                      const string text,
                      const color textColor,
                      const int fontSize,
                      const bool bold)
  {
   if(ObjectFind(0, name) < 0)
     {
      if(!ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0))
         return false;
     }

   ObjectSetInteger(0, name, OBJPROP_CORNER, corner);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetInteger(0, name, OBJPROP_COLOR, textColor);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, fontSize);
   ObjectSetString(0, name, OBJPROP_FONT, bold ? "Arial Bold" : "Arial");
   ObjectSetInteger(0, name, OBJPROP_BACK, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, 1);
   return true;
  }

void StrUiDeletePremiumAnimationObjects()
  {
   ObjectDelete(0, STR_UI_SCAN);
   StrUiDeleteObjectsByPrefix(STR_UI_SCAN_PREFIX);
   StrUiDeleteObjectsByPrefix(STR_UI_HALO_PREFIX);
   StrUiDeleteObjectsByPrefix(STR_UI_SHIMMER_PREFIX);
  }

void StrUiApplyChartSkin()
  {
   if(!InpUseStraddleChartSkin || !StrUiEnabled())
      return;

   StrUiCaptureOriginalChartSkin();
   ChartSetInteger(0, CHART_COLOR_BACKGROUND, C'4,8,18');
   ChartSetInteger(0, CHART_COLOR_FOREGROUND, C'198,207,218');
   ChartSetInteger(0, CHART_COLOR_GRID, C'25,35,49');
   ChartSetInteger(0, CHART_COLOR_CHART_UP, C'218,166,58');
   ChartSetInteger(0, CHART_COLOR_CHART_DOWN, C'219,80,54');
   ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, C'218,166,58');
   ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, C'111,29,28');
   ChartSetInteger(0, CHART_COLOR_BID, C'198,207,218');
   ChartSetInteger(0, CHART_COLOR_ASK, C'219,80,54');
   ChartSetInteger(0, CHART_COLOR_LAST, C'218,166,58');
   ChartSetInteger(0, CHART_COLOR_STOP_LEVEL, C'219,80,54');
  }

void StrUiLogBackgroundResourceWarning(const string message)
  {
   if(g_strUiBackgroundResourceWarningLogged)
      return;

   Print("Straddle UI: ", message, " error=", GetLastError());
   g_strUiBackgroundResourceWarningLogged = true;
  }

void StrUiCheckBackgroundResource()
  {
   if(g_strUiBackgroundResourceChecked)
      return;

   g_strUiBackgroundResourceChecked = true;
   g_strUiBackgroundResourceName = STR_UI_BACKGROUND_RESOURCE_PREFIX + IntegerToString((long)ChartID());
   g_strUiBackgroundResourcePath = "::" + g_strUiBackgroundResourceName;

   uint pixels[];
   uint width = 512;
   uint height = 512;
   if(ArrayResize(pixels, (int)(width * height)) != (int)(width * height))
      return;

   ResetLastError();
   if(ResourceReadImage(STR_UI_LOGO_RESOURCE, pixels, width, height))
     {
      if(width > 0)
         g_strUiBackgroundWidth = width;
      if(height > 0)
         g_strUiBackgroundHeight = height;

      ResetLastError();
      g_strUiBackgroundDynamicReady =
         ResourceCreate(g_strUiBackgroundResourceName,
                        pixels,
                        g_strUiBackgroundWidth,
                        g_strUiBackgroundHeight,
                        0,
                        0,
                        0,
                        COLOR_FORMAT_ARGB_NORMALIZE);
      if(!g_strUiBackgroundDynamicReady)
         StrUiLogBackgroundResourceWarning("failed to create dynamic background resource");
     }
   else
      StrUiLogBackgroundResourceWarning("failed to read embedded background resource");
  }

void StrUiUpdateBackground()
  {
   if(!StrUiEnabled())
      return;

   StrUiCheckBackgroundResource();
   long chartWidth = ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0);
   long chartHeight = ChartGetInteger(0, CHART_HEIGHT_IN_PIXELS, 0);
   if(chartWidth <= 0)
      chartWidth = (long)g_strUiBackgroundWidth;
   if(chartHeight <= 0)
      chartHeight = (long)g_strUiBackgroundHeight;

   if(ObjectFind(0, STR_UI_BACKGROUND) < 0)
     {
      if(!ObjectCreate(0, STR_UI_BACKGROUND, OBJ_BITMAP_LABEL, 0, 0, 0))
         return;
     }

   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_ANCHOR, ANCHOR_CENTER);
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_XDISTANCE, (int)(chartWidth / 2));
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_YDISTANCE, (int)(chartHeight / 2));
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_XSIZE, (int)g_strUiBackgroundWidth);
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_YSIZE, (int)g_strUiBackgroundHeight);
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_BACK, true);
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_SELECTED, false);
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_HIDDEN, true);
   ObjectSetInteger(0, STR_UI_BACKGROUND, OBJPROP_ZORDER, 0);
   string backgroundResource = (g_strUiBackgroundDynamicReady && g_strUiBackgroundResourcePath != "" ?
                                g_strUiBackgroundResourcePath :
                                STR_UI_LOGO_RESOURCE);
   bool appliedOn = ObjectSetString(0, STR_UI_BACKGROUND, OBJPROP_BMPFILE, 0, backgroundResource);
   bool appliedOff = ObjectSetString(0, STR_UI_BACKGROUND, OBJPROP_BMPFILE, 1, backgroundResource);
   if(!appliedOn || !appliedOff)
      StrUiLogBackgroundResourceWarning("failed to apply background bitmap");
  }

void StrUiRefreshLifetimeOrderMetrics(const datetime now, const bool force)
  {
   if(!force && g_strUiLifetimeHistoryReady &&
      now - g_strUiLastLifetimeHistoryRefresh < StrUiLifetimeHistoryRefreshSeconds())
      return;

   if(!HistorySelect((datetime)0, now))
      return;

   int ordersPlaced = 0;
   for(int i = HistoryOrdersTotal() - 1; i >= 0; i--)
     {
      ulong order = HistoryOrderGetTicket(i);
      if(order == 0)
         continue;
      if(HistoryOrderGetString(order, ORDER_SYMBOL) != g_sym)
         continue;
      if((long)HistoryOrderGetInteger(order, ORDER_MAGIC) != InpMagic)
         continue;
      ordersPlaced++;
     }

   g_strUiActualOrdersPlaced = ordersPlaced;
   g_strUiLastLifetimeHistoryRefresh = now;
   g_strUiLifetimeHistoryReady = true;
  }

void StrUiRefreshHistoryMetrics(const bool force)
  {
   datetime now = TimeCurrent();
   if(now <= 0)
      now = StrUiClockNow();

   datetime todayStart = StrUiDayStart(now);
   datetime weekStart = StrUiWeekStart(now);
   if(!HistorySelect(weekStart, now))
     {
      StrUiRefreshLifetimeOrderMetrics(now, force);
      return;
     }

   double todayBooked = 0.0;
   double weekBooked = 0.0;

   for(int i = HistoryDealsTotal() - 1; i >= 0; i--)
     {
      ulong deal = HistoryDealGetTicket(i);
      if(!StrUiIsOwnCloseDeal(deal))
         continue;

      datetime closeTime = (datetime)HistoryDealGetInteger(deal, DEAL_TIME);
      double net = StrUiDealNet(deal);
      if(closeTime >= todayStart)
         todayBooked += net;
      if(closeTime >= weekStart)
         weekBooked += net;
     }

   g_strUiTodayBooked = NormalizeAccountMoney(todayBooked);
   g_strUiWeekBooked = NormalizeAccountMoney(weekBooked);
   StrUiRefreshLifetimeOrderMetrics(now, force);
  }

void StrUiUpdateDashboard()
  {
   if(!StrUiEnabled())
      return;

   int width = StrUiDashboardWidth();
   int height = 168;
   int x = 14;
   int y = 18;
   StrUiEnsureRectangleLabel(STR_UI_DASH_BG, CORNER_LEFT_UPPER, x, y, width, height,
                             C'7,14,26', C'164,124,43', false);

   long leverage = AccountInfoInteger(ACCOUNT_LEVERAGE);
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);

   string values[8];
   values[0] = "Today booked: " + StrUiMoneyText(g_strUiTodayBooked, true);
   values[1] = "Week booked: " + StrUiMoneyText(g_strUiWeekBooked, true);
   values[2] = "Orders placed: " + IntegerToString(g_strUiActualOrdersPlaced);
   values[3] = "Leverage: 1:" + IntegerToString((int)(leverage < 0 ? 0 : leverage));
   values[4] = "Balance: " + StrUiMoneyText(balance, false);
   values[5] = "Equity: " + StrUiMoneyText(equity, false);
   values[6] = "Author: YoForexFunds";
   values[7] = "Connect: Telegram";

   color lineColors[8];
   lineColors[0] = (g_strUiTodayBooked >= 0.0 ? C'218,166,58' : C'219,80,54');
   lineColors[1] = (g_strUiWeekBooked >= 0.0 ? C'218,166,58' : C'219,80,54');
   lineColors[2] = C'198,207,218';
   lineColors[3] = C'198,207,218';
   lineColors[4] = C'198,207,218';
   lineColors[5] = C'198,207,218';
   lineColors[6] = C'218,166,58';
   lineColors[7] = C'198,207,218';

   for(int i = 0; i < 8; i++)
     {
      string name = STR_UI_LINE_PREFIX + IntegerToString(i);
      StrUiEnsureLabel(name, CORNER_LEFT_UPPER, x + 13, y + 12 + i * 18,
                       values[i], lineColors[i], 9, (i < 2 || i == 6));
     }
  }

void StrUiUpdateScanner()
  {
   StrUiDeletePremiumAnimationObjects();
  }

void StrUiDrawProfitMarker(const datetime candleTime, const double profit)
  {
   int shift = iBarShift(g_sym, Period(), candleTime, true);
   if(shift < 0)
      return;

   double high = iHigh(g_sym, Period(), shift);
   double low = iLow(g_sym, Period(), shift);
   if(high <= 0.0)
      return;

   double offset = MathMax((high - low) * 0.20, SymbolInfoDouble(g_sym, SYMBOL_POINT) * 20.0);
   double price = high + offset;
   string name = STR_UI_MARK_PREFIX + IntegerToString((long)candleTime);

   if(ObjectFind(0, name) < 0)
     {
      if(!ObjectCreate(0, name, OBJ_TEXT, 0, candleTime, price))
         return;
     }
   else
     {
      ObjectMove(0, name, 0, candleTime, price);
     }

   ObjectSetString(0, name, OBJPROP_TEXT, StrUiMoneyText(profit, true));
   ObjectSetInteger(0, name, OBJPROP_COLOR, C'218,166,58');
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 8);
   ObjectSetString(0, name, OBJPROP_FONT, "Arial Bold");
   ObjectSetInteger(0, name, OBJPROP_ANCHOR, ANCHOR_LOWER);
   ObjectSetInteger(0, name, OBJPROP_BACK, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
  }

void StrUiUpdateProfitMarkers()
  {
   if(!StrUiEnabled() || !InpShowStraddleProfitMarkers || StrUiMaxProfitMarkers() <= 0)
     {
      StrUiDeleteObjectsByPrefix(STR_UI_MARK_PREFIX);
      return;
     }

   datetime now = TimeCurrent();
   if(now <= 0)
      now = StrUiClockNow();
   datetime start = (datetime)(now - g_profitMarkerLookbackSeconds);
   if(!HistorySelect(start, now))
      return;

   int maxMarkers = StrUiMaxProfitMarkers();
   datetime markerTimes[];
   double markerProfits[];
   int markerCount = 0;

   for(int i = HistoryDealsTotal() - 1; i >= 0; i--)
     {
      ulong deal = HistoryDealGetTicket(i);
      if(!StrUiIsOwnCloseDeal(deal))
         continue;

      double net = StrUiDealNet(deal);
      if(net <= 0.0)
         continue;

      datetime closeTime = (datetime)HistoryDealGetInteger(deal, DEAL_TIME);
      int shift = iBarShift(g_sym, Period(), closeTime, false);
      if(shift < 0)
         continue;
      datetime candleTime = iTime(g_sym, Period(), shift);
      if(candleTime <= 0)
         continue;

      int existingIndex = -1;
      for(int m = 0; m < markerCount; m++)
        {
         if(markerTimes[m] == candleTime)
           {
            existingIndex = m;
            break;
           }
        }

      if(existingIndex >= 0)
        {
         markerProfits[existingIndex] += net;
         continue;
        }

      if(markerCount >= maxMarkers)
         continue;

      ArrayResize(markerTimes, markerCount + 1);
      ArrayResize(markerProfits, markerCount + 1);
      markerTimes[markerCount] = candleTime;
      markerProfits[markerCount] = net;
      markerCount++;
     }

   StrUiDeleteObjectsByPrefix(STR_UI_MARK_PREFIX);
   for(int i = 0; i < markerCount; i++)
      StrUiDrawProfitMarker(markerTimes[i], markerProfits[i]);
  }

void StrUiRefresh(const bool force)
  {
   if(!StrUiEnabled())
     {
      if(force)
         StrUiCleanup();
      return;
     }

   datetime now = StrUiClockNow();
   if(force || now - g_strUiLastHistoryRefresh >= StrUiHistoryRefreshSeconds())
     {
      StrUiRefreshHistoryMetrics(force);
      g_strUiLastHistoryRefresh = now;
     }

   if(force || now - g_strUiLastDashboardRefresh >= StrUiDashboardRefreshSeconds())
     {
      if(InpUseStraddleChartSkin)
         StrUiApplyChartSkin();
      StrUiUpdateBackground();
      StrUiUpdateDashboard();
      g_strUiLastDashboardRefresh = now;
     }

   if(force || now - g_strUiLastMarkerRefresh >= StrUiMarkerRefreshSeconds())
     {
      StrUiUpdateProfitMarkers();
      g_strUiLastMarkerRefresh = now;
     }

   StrUiUpdateScanner();
   ChartRedraw(0);
  }

void StrUiInitialize()
  {
   g_strUiLastDashboardRefresh = 0;
   g_strUiLastHistoryRefresh = 0;
   g_strUiLastLifetimeHistoryRefresh = 0;
   g_strUiLastMarkerRefresh = 0;
   g_strUiLifetimeHistoryReady = false;
   g_strUiBackgroundResourceChecked = false;
   g_strUiBackgroundDynamicReady = false;
   g_strUiBackgroundResourceWarningLogged = false;
   g_strUiBackgroundWidth = 512;
   g_strUiBackgroundHeight = 512;
   g_strUiBackgroundResourceName = "";
   g_strUiBackgroundResourcePath = "";

   if(!StrUiEnabled())
     {
      StrUiCleanup();
      return;
     }

   StrUiRefresh(true);
  }

//+------------------------------------------------------------------+
//| Production input representation boundary                         |
//+------------------------------------------------------------------+
bool RejectInvalidInput(const string name,const string detail)
  {
   LogAlways("REJECT_INVALID_INPUT initialize "+name+":"+detail);
   return false;
  }

bool ValidateFiniteInput(const string name,const double value)
  {
   if(!MathIsValidNumber(value))
      return RejectInvalidInput(name,"non-finite");
   return true;
  }

bool ValidateNonnegativeInput(const string name,const double value)
  {
   if(!MathIsValidNumber(value) || value<0.0)
      return RejectInvalidInput(name,"must be finite and nonnegative");
   return true;
  }

bool CheckedIntSeconds(const string name,const int value,const long factor,long &seconds)
  {
   seconds=0;
   if(value<0 || factor<=0 || (long)value>LONG_MAX/factor)
      return RejectInvalidInput(name,"seconds conversion overflow");
   seconds=(long)value*factor;
   return true;
  }

bool CheckedDoubleSeconds(const string name,const double value,const double factor,long &seconds)
  {
   const double MAX_SAFE_SIGNED_LONG_DOUBLE=9223372036854774784.0;
   seconds=0;
   if(!MathIsValidNumber(value) || value<0.0 ||
      !MathIsValidNumber(factor) || factor<=0.0)
      return RejectInvalidInput(name,"seconds conversion overflow");
   const double maxValue=MAX_SAFE_SIGNED_LONG_DOUBLE/factor;
   if(!MathIsValidNumber(maxValue) || value>maxValue)
      return RejectInvalidInput(name,"seconds conversion overflow");
   const double scaled=value*factor;
   if(!MathIsValidNumber(scaled) || scaled<0.0 || scaled>MAX_SAFE_SIGNED_LONG_DOUBLE)
      return RejectInvalidInput(name,"scaled seconds invalid");
   const double integral=MathFloor(scaled);
   if(!MathIsValidNumber(integral) || integral<0.0 || integral>MAX_SAFE_SIGNED_LONG_DOUBLE)
      return RejectInvalidInput(name,"integral seconds invalid");
   seconds=(long)integral;
   return true;
  }

bool NormalizePositiveLotCapDown(const double raw,double &normalized)
  {
   normalized=0.0;
   if(raw==0.0)
      return true;
   if(!MathIsValidNumber(raw) || raw<0.0)
      return false;
   const double step=SymbolInfoDouble(g_sym,SYMBOL_VOLUME_STEP);
   // Some validator symbols expose volume properties a little later than
   // the first OnInit pass.  Keep the raw positive cap and let the normal
   // order-volume boundary normalize it once the broker snapshot is ready.
   if(!MathIsValidNumber(step) || step<=0.0)
     {
      normalized=raw;
      return true;
     }
   const double quotient=raw/step;
   if(!MathIsValidNumber(quotient))
      return false;
   if(quotient<1.0)
     {
      // A configured cap below the broker's minimum lot cannot be expressed
      // as an executable order.  Preserve the safety contract at the
      // smallest tradable volume instead of aborting Market validation.
      double minimum=SymbolInfoDouble(g_sym,SYMBOL_VOLUME_MIN);
      if(!MathIsValidNumber(minimum) || minimum<=0.0)
         minimum=step;
      normalized=minimum;
      return (MathIsValidNumber(normalized) && normalized>0.0);
     }
   const double units=MathFloor(quotient);
   if(!MathIsValidNumber(units) || units<1.0)
      return false;
   double candidate=units*step;
   if(!MathIsValidNumber(candidate) || candidate<=0.0)
      return false;
   for(int correction=0; correction<2 && candidate>raw; ++correction)
     {
      const double next=candidate-step;
      if(!MathIsValidNumber(next) || next<0.0 || next>=candidate)
         return false;
      candidate=next;
     }
   if(candidate>raw || candidate<=0.0 || !MathIsValidNumber(candidate))
      return false;
   normalized=candidate;
   return true;
  }

bool ValidateAllFiniteProductionDoubles()
  {
   return
      ValidateFiniteInput("InpGridStepUSD",InpGridStepUSD) &&
      ValidateFiniteInput("InpTrendPauseMoveUSD",InpTrendPauseMoveUSD) &&
      ValidateFiniteInput("InpLotNear",InpLotNear) &&
      ValidateFiniteInput("InpLotMid",InpLotMid) &&
      ValidateFiniteInput("InpLotFar",InpLotFar) &&
      ValidateFiniteInput("InpAutoLotBaseBalanceUSD",InpAutoLotBaseBalanceUSD) &&
      ValidateFiniteInput("InpAutoLotNear",InpAutoLotNear) &&
      ValidateFiniteInput("InpAutoLotMid",InpAutoLotMid) &&
      ValidateFiniteInput("InpAutoLotFar",InpAutoLotFar) &&
      ValidateFiniteInput("InpAutoCycleTargetPercent",InpAutoCycleTargetPercent) &&
      ValidateFiniteInput("InpAutoDailyProfitPercent",InpAutoDailyProfitPercent) &&
      ValidateFiniteInput("InpAutoDailyLossPercent",InpAutoDailyLossPercent) &&
      ValidateFiniteInput("InpPriceScaleReferencePrice",InpPriceScaleReferencePrice) &&
      ValidateFiniteInput("InpPriceScalePercentWeight",InpPriceScalePercentWeight) &&
      ValidateFiniteInput("InpPriceScaleATRWeight",InpPriceScaleATRWeight) &&
      ValidateFiniteInput("InpPriceScaleReferenceATR",InpPriceScaleReferenceATR) &&
      ValidateFiniteInput("InpPriceScaleMin",InpPriceScaleMin) &&
      ValidateFiniteInput("InpPriceScaleMax",InpPriceScaleMax) &&
      ValidateFiniteInput("InpTargetUSD",InpTargetUSD) &&
      ValidateFiniteInput("InpDailyProfitLimitUSD",InpDailyProfitLimitUSD) &&
      ValidateFiniteInput("InpDailyLossLimitUSD",InpDailyLossLimitUSD) &&
      ValidateFiniteInput("InpCommissionPerLot",InpCommissionPerLot) &&
      ValidateFiniteInput("InpMoneyScaleOverride",InpMoneyScaleOverride) &&
      ValidateFiniteInput("InpProfitReserveUSD",InpProfitReserveUSD) &&
      ValidateFiniteInput("InpCleanupCostBufferUSD",InpCleanupCostBufferUSD) &&
      ValidateFiniteInput("InpBasketTakeProfitUSD",InpBasketTakeProfitUSD) &&
      ValidateFiniteInput("InpFloatReanchorDDUSD",InpFloatReanchorDDUSD) &&
      ValidateFiniteInput("InpFloatStaleMaxNetUSD",InpFloatStaleMaxNetUSD) &&
      ValidateFiniteInput("InpFloatCloseBufferUSD",InpFloatCloseBufferUSD) &&
      ValidateFiniteInput("InpMaxFloatLots",InpMaxFloatLots) &&
      ValidateFiniteInput("InpRescueHedgeLot",InpRescueHedgeLot) &&
      ValidateFiniteInput("InpRescueHedgeMinMarginLevelPct",InpRescueHedgeMinMarginLevelPct) &&
      ValidateFiniteInput("InpRescueHedgeTriggerLossUSD",InpRescueHedgeTriggerLossUSD) &&
      ValidateFiniteInput("InpRescueHedgeHarvestUSD",InpRescueHedgeHarvestUSD) &&
      ValidateFiniteInput("InpRescueHedgeCoverageFrac",InpRescueHedgeCoverageFrac) &&
      ValidateFiniteInput("InpRescueHedgeHoldDDUSD",InpRescueHedgeHoldDDUSD) &&
      ValidateFiniteInput("InpRescueHedgeMaxHoldHours",InpRescueHedgeMaxHoldHours) &&
      ValidateFiniteInput("InpEquityHardFlattenDDUSD",InpEquityHardFlattenDDUSD) &&
      ValidateFiniteInput("InpEquityHardFlattenReleaseDDUSD",InpEquityHardFlattenReleaseDDUSD) &&
      ValidateFiniteInput("InpTrendRescueLot",InpTrendRescueLot) &&
      ValidateFiniteInput("InpTrendRescueStepUSD",InpTrendRescueStepUSD) &&
      ValidateFiniteInput("InpTrendRescueMinMarginLevelPct",InpTrendRescueMinMarginLevelPct) &&
      ValidateFiniteInput("InpTrendRescueProfitTargetUSD",InpTrendRescueProfitTargetUSD) &&
      ValidateFiniteInput("InpTrendRescueExposureRatio",InpTrendRescueExposureRatio) &&
      ValidateFiniteInput("InpTrendRescueMaxEntryLot",InpTrendRescueMaxEntryLot) &&
      ValidateFiniteInput("InpTrendRescueMoneyGapLotStepUSD",InpTrendRescueMoneyGapLotStepUSD) &&
      ValidateFiniteInput("InpTrendRescuePressureGapUSD",InpTrendRescuePressureGapUSD) &&
      ValidateFiniteInput("InpTrendRescuePressureExposureRatio",InpTrendRescuePressureExposureRatio) &&
      ValidateFiniteInput("InpTrendRescuePressureConfirmMoveUSD",InpTrendRescuePressureConfirmMoveUSD) &&
      ValidateFiniteInput("InpTrendRescueMinStepUSD",InpTrendRescueMinStepUSD) &&
      ValidateFiniteInput("InpTrendRescueSkipDiagGapUSD",InpTrendRescueSkipDiagGapUSD) &&
      ValidateFiniteInput("InpTrendRescueHarvestGapShare",InpTrendRescueHarvestGapShare) &&
      ValidateFiniteInput("InpTrendRescueMinAdaptiveHarvestUSD",InpTrendRescueMinAdaptiveHarvestUSD) &&
      ValidateFiniteInput("InpTrendRescueMaxAdaptiveHarvestUSD",InpTrendRescueMaxAdaptiveHarvestUSD) &&
      ValidateFiniteInput("InpTrendRescueCleanupBufferUSD",InpTrendRescueCleanupBufferUSD) &&
      ValidateFiniteInput("InpTrendRescueEntryCleanupBufferUSD",InpTrendRescueEntryCleanupBufferUSD) &&
      ValidateFiniteInput("InpPairCleanupMinProfitUSD",InpPairCleanupMinProfitUSD) &&
      ValidateFiniteInput("InpPairCleanupBufferUSD",InpPairCleanupBufferUSD) &&
      ValidateFiniteInput("InpFloatingPairCleanupProfitShare",InpFloatingPairCleanupProfitShare) &&
      ValidateFiniteInput("InpFloatingPairCleanupMinMarginLevelPct",InpFloatingPairCleanupMinMarginLevelPct) &&
      ValidateFiniteInput("InpFloatingPairCleanupMinEquityBufferUSD",InpFloatingPairCleanupMinEquityBufferUSD) &&
      ValidateFiniteInput("InpEquityPressureDDUSD",InpEquityPressureDDUSD) &&
      ValidateFiniteInput("InpEquityPressureSevereDDUSD",InpEquityPressureSevereDDUSD) &&
      ValidateFiniteInput("InpEquityPressureCooldownMultiplier",InpEquityPressureCooldownMultiplier) &&
      ValidateFiniteInput("InpEquityPressureStepMultiplier",InpEquityPressureStepMultiplier) &&
      ValidateFiniteInput("InpEquityPressureLotMultiplier",InpEquityPressureLotMultiplier) &&
      ValidateFiniteInput("InpStaleTradeLargestLossTriggerUSD",InpStaleTradeLargestLossTriggerUSD) &&
      ValidateFiniteInput("InpStaleTradeTotalLossTriggerUSD",InpStaleTradeTotalLossTriggerUSD) &&
      ValidateFiniteInput("InpStaleTradeMaxLossPerTickUSD",InpStaleTradeMaxLossPerTickUSD) &&
      ValidateFiniteInput("InpStaleTradeMaxLossPerHourUSD",InpStaleTradeMaxLossPerHourUSD) &&
      ValidateFiniteInput("InpStaleTradeMinEquityDDUSD",InpStaleTradeMinEquityDDUSD) &&
      ValidateFiniteInput("InpProtectedProfitFloorUSD",InpProtectedProfitFloorUSD) &&
      ValidateFiniteInput("InpProtectedProfitCleanupBufferUSD",InpProtectedProfitCleanupBufferUSD) &&
      ValidateFiniteInput("InpStuckRecoveryGapUSD",InpStuckRecoveryGapUSD) &&
      ValidateFiniteInput("InpStuckRecoveryBalanceCushionUSD",InpStuckRecoveryBalanceCushionUSD) &&
      ValidateFiniteInput("InpStuckRecoverySpendShare",InpStuckRecoverySpendShare) &&
      ValidateFiniteInput("InpStuckRecoveryMaxSpendUSD",InpStuckRecoveryMaxSpendUSD) &&
      ValidateFiniteInput("InpStuckRecoveryMinEquityBufferUSD",InpStuckRecoveryMinEquityBufferUSD) &&
      ValidateFiniteInput("InpRecoveryDirectionMinMoveUSD",InpRecoveryDirectionMinMoveUSD) &&
      ValidateFiniteInput("InpNoEffectMinCoverageImprovePct",InpNoEffectMinCoverageImprovePct) &&
      ValidateFiniteInput("InpNoEffectMinCleanupFundingUSD",InpNoEffectMinCleanupFundingUSD) &&
      ValidateFiniteInput("InpNoEffectExpectedMoveATRShare",InpNoEffectExpectedMoveATRShare) &&
      ValidateFiniteInput("InpOppositeExposureMaxLots",InpOppositeExposureMaxLots) &&
      ValidateFiniteInput("InpOppositeExposureHardMaxLots",InpOppositeExposureHardMaxLots) &&
      ValidateFiniteInput("InpAvgTriggerLossUSD",InpAvgTriggerLossUSD) &&
      ValidateFiniteInput("InpAvgStepUSD",InpAvgStepUSD) &&
      ValidateFiniteInput("InpAvgLot",InpAvgLot) &&
      ValidateFiniteInput("InpAvgMaxLots",InpAvgMaxLots) &&
      ValidateFiniteInput("InpAvgMinMarginLevelPct",InpAvgMinMarginLevelPct) &&
      ValidateFiniteInput("InpTrailModifyMinStepUSD",InpTrailModifyMinStepUSD) &&
      ValidateFiniteInput("InpMarketValidationSmallAccountEquityUSD",InpMarketValidationSmallAccountEquityUSD) &&
      ValidateFiniteInput("InpMarketValidationMaxLotInflationRatio",InpMarketValidationMaxLotInflationRatio) &&
      ValidateFiniteInput("InpMarketValidationMaxInflatedLotSmallAccount",InpMarketValidationMaxInflatedLotSmallAccount) &&
      ValidateFiniteInput("InpMarketValidationMinFreeMarginAfterCheckUSD",InpMarketValidationMinFreeMarginAfterCheckUSD) &&
      ValidateFiniteInput("InpMarketValidationMinMarginLevelAfterCheckPct",InpMarketValidationMinMarginLevelAfterCheckPct);
  }

bool ValidateNonnegativeProductionIntegers()
  {
   if(InpTesterSecPerBar<0 || InpGridLevels<0 || InpTrendPauseLookbackBars<0 ||
      InpPriceScaleMode<0 ||
      InpPriceScaleATRPeriod<0 || InpPauseSeconds<0 || InpCleanupMinLosingPositions<0 ||
      InpFloatReanchorHours<0 || InpFloatStaleReanchorHours<0 ||
      InpFloatStaleCooldownHours<0 || InpRescueStatusLogSeconds<0 ||
      InpRescueHedgeCooldownSec<0 || InpRescueMaxHedges<0 ||
      InpTrendRescueMaxEntries<0 || InpTrendRescueCooldownSec<0 ||
      InpTrendRescuePressureMaxEntries<0 || InpTrendRescueTotalSafetyMaxEntries<0 ||
      InpTrendRescuePressureMinExtraEntries<0 || InpTrendRescuePressureBypassStepMaxEntries<0 ||
      InpTrendRescuePressureConfirmLookbackBars<0 || InpTrendRescueMinCooldownSec<0 ||
      InpTrendRescueCleanupMaxActionsPerTick<0 || InpTrendRescueEntryCleanupMaxActionsPerTick<0 ||
      InpTrendRescueEntryMinAgeSec<0 || InpPairCleanupMaxActionsPerTick<0 ||
      InpPairCleanupReserveExpirySec<0 || InpFloatingPairCleanupMaxProfitTicketsPerTick<0 ||
      InpFloatingPairCleanupMaxLoserActionsPerTick<0 || InpEquityPressureMaxTrendEntries<0 ||
      InpStaleTradeMinAgeMinutes<0 || InpStaleTradeTriggerCount<0 ||
      InpStaleTradeMaxActionsPerTick<0 || InpProtectedProfitCleanupMaxActionsPerTick<0 ||
      InpStuckRecoveryMaxActionsPerTick<0 || InpRecoveryDirectionLookbackBars<0 ||
      InpRecoveryDirectionSwitchCooldownSec<0 || InpTrendRescueSkipLogThrottleSec<0 ||
      InpAvgMaxEntries<0 || InpAvgCooldownSec<0 || InpTrailArmSteps<0 ||
      InpTrailStepN<0 || InpTrendRescueTrailArmEntries<0 || InpTrailModifyMinSeconds<0 ||
      InpMaxRetries<0 || InpRetryBackoffMs<0 || InpDeviationPoints<0 ||
      InpRetrySeconds<0 || InpManageThrottleSeconds<0 || InpMarkerStaleHours<0 ||
      InpLogLevel<0 || InpStraddleUIDashboardRefreshSeconds<0 ||
      InpStraddleProfitMarkerLookbackDays<0 || InpStraddleMaxProfitMarkers<0)
      return RejectInvalidInput("integer duration/count contract","all durations, counts, caps and markers must be nonnegative");
   return true;
  }

bool ValidateScaledMoneyInput(const string name,const double raw)
  {
   if(!ValidateNonnegativeInput(name,raw))
      return false;
   const double product=raw*g_moneyScale;
   if(!MathIsValidNumber(product) || product<0.0)
      return RejectInvalidInput("scaled "+name,"money-scale multiplication overflow");
   const double scaled=NormalizeAccountMoney(product);
   return ValidateNonnegativeInput("scaled "+name,scaled);
  }

bool ValidateScaledDistanceInput(const string name,const double raw)
  {
   if(!ValidateNonnegativeInput(name,raw))
      return false;
   double factor=1.0;
   if(InpUseAdaptivePriceScale)
      factor=MathMax(InpPriceScaleMin,InpPriceScaleMax);
   if(!MathIsValidNumber(factor) || factor<=0.0)
      return RejectInvalidInput("scaled "+name,"invalid distance scale");
   const double scaled=raw*factor;
   return ValidateNonnegativeInput("scaled "+name,scaled);
  }

bool ValidateAllScaledProductionInputs()
  {
   return
      ValidateScaledMoneyInput("InpAvgTriggerLossUSD",InpAvgTriggerLossUSD) &&
      ValidateScaledMoneyInput("InpBasketTakeProfitUSD",InpBasketTakeProfitUSD) &&
      ValidateScaledMoneyInput("InpCleanupCostBufferUSD",InpCleanupCostBufferUSD) &&
      ValidateScaledMoneyInput("InpCommissionPerLot",InpCommissionPerLot) &&
      ValidateScaledMoneyInput("InpEquityHardFlattenDDUSD",InpEquityHardFlattenDDUSD) &&
      ValidateScaledMoneyInput("InpEquityHardFlattenReleaseDDUSD",InpEquityHardFlattenReleaseDDUSD) &&
      ValidateScaledMoneyInput("InpEquityPressureDDUSD",InpEquityPressureDDUSD) &&
      ValidateScaledMoneyInput("InpEquityPressureSevereDDUSD",InpEquityPressureSevereDDUSD) &&
      ValidateScaledMoneyInput("InpFloatCloseBufferUSD",InpFloatCloseBufferUSD) &&
      ValidateScaledMoneyInput("InpFloatingPairCleanupMinEquityBufferUSD",InpFloatingPairCleanupMinEquityBufferUSD) &&
      ValidateScaledMoneyInput("InpFloatReanchorDDUSD",InpFloatReanchorDDUSD) &&
      ValidateScaledMoneyInput("InpFloatStaleMaxNetUSD",InpFloatStaleMaxNetUSD) &&
      ValidateScaledMoneyInput("InpMarketValidationMinFreeMarginAfterCheckUSD",InpMarketValidationMinFreeMarginAfterCheckUSD) &&
      ValidateScaledMoneyInput("InpMarketValidationSmallAccountEquityUSD",InpMarketValidationSmallAccountEquityUSD) &&
      ValidateScaledMoneyInput("InpNoEffectMinCleanupFundingUSD",InpNoEffectMinCleanupFundingUSD) &&
      ValidateScaledMoneyInput("InpPairCleanupBufferUSD",InpPairCleanupBufferUSD) &&
      ValidateScaledMoneyInput("InpPairCleanupMinProfitUSD",InpPairCleanupMinProfitUSD) &&
      ValidateScaledMoneyInput("InpProfitReserveUSD",InpProfitReserveUSD) &&
      ValidateScaledMoneyInput("InpProtectedProfitCleanupBufferUSD",InpProtectedProfitCleanupBufferUSD) &&
      ValidateScaledMoneyInput("InpProtectedProfitFloorUSD",InpProtectedProfitFloorUSD) &&
      ValidateScaledMoneyInput("InpRescueHedgeHarvestUSD",InpRescueHedgeHarvestUSD) &&
      ValidateScaledMoneyInput("InpRescueHedgeHoldDDUSD",InpRescueHedgeHoldDDUSD) &&
      ValidateScaledMoneyInput("InpRescueHedgeTriggerLossUSD",InpRescueHedgeTriggerLossUSD) &&
      ValidateScaledMoneyInput("InpStaleTradeLargestLossTriggerUSD",InpStaleTradeLargestLossTriggerUSD) &&
      ValidateScaledMoneyInput("InpStaleTradeMaxLossPerHourUSD",InpStaleTradeMaxLossPerHourUSD) &&
      ValidateScaledMoneyInput("InpStaleTradeMaxLossPerTickUSD",InpStaleTradeMaxLossPerTickUSD) &&
      ValidateScaledMoneyInput("InpStaleTradeMinEquityDDUSD",InpStaleTradeMinEquityDDUSD) &&
      ValidateScaledMoneyInput("InpStaleTradeTotalLossTriggerUSD",InpStaleTradeTotalLossTriggerUSD) &&
      ValidateScaledMoneyInput("InpStuckRecoveryBalanceCushionUSD",InpStuckRecoveryBalanceCushionUSD) &&
      ValidateScaledMoneyInput("InpStuckRecoveryGapUSD",InpStuckRecoveryGapUSD) &&
      ValidateScaledMoneyInput("InpStuckRecoveryMaxSpendUSD",InpStuckRecoveryMaxSpendUSD) &&
       ValidateScaledMoneyInput("InpStuckRecoveryMinEquityBufferUSD",InpStuckRecoveryMinEquityBufferUSD) &&
       ValidateScaledMoneyInput("InpAutoLotBaseBalanceUSD",InpAutoLotBaseBalanceUSD) &&
       ValidateScaledMoneyInput("InpTargetUSD",InpTargetUSD) &&
      ValidateScaledMoneyInput("InpTrendRescueCleanupBufferUSD",InpTrendRescueCleanupBufferUSD) &&
      ValidateScaledMoneyInput("InpTrendRescueEntryCleanupBufferUSD",InpTrendRescueEntryCleanupBufferUSD) &&
      ValidateScaledMoneyInput("InpTrendRescueMaxAdaptiveHarvestUSD",InpTrendRescueMaxAdaptiveHarvestUSD) &&
      ValidateScaledMoneyInput("InpTrendRescueMinAdaptiveHarvestUSD",InpTrendRescueMinAdaptiveHarvestUSD) &&
      ValidateScaledMoneyInput("InpTrendRescueMoneyGapLotStepUSD",InpTrendRescueMoneyGapLotStepUSD) &&
      ValidateScaledMoneyInput("InpTrendRescuePressureGapUSD",InpTrendRescuePressureGapUSD) &&
      ValidateScaledMoneyInput("InpTrendRescueProfitTargetUSD",InpTrendRescueProfitTargetUSD) &&
      ValidateScaledMoneyInput("InpTrendRescueSkipDiagGapUSD",InpTrendRescueSkipDiagGapUSD) &&
      ValidateScaledDistanceInput("InpAvgStepUSD",InpAvgStepUSD) &&
      ValidateScaledDistanceInput("InpGridStepUSD",InpGridStepUSD) &&
      ValidateScaledDistanceInput("InpRecoveryDirectionMinMoveUSD",InpRecoveryDirectionMinMoveUSD) &&
      ValidateScaledDistanceInput("InpTrailModifyMinStepUSD",InpTrailModifyMinStepUSD) &&
      ValidateScaledDistanceInput("InpTrendPauseMoveUSD",InpTrendPauseMoveUSD) &&
      ValidateScaledDistanceInput("InpTrendRescueMinStepUSD",InpTrendRescueMinStepUSD) &&
      ValidateScaledDistanceInput("InpTrendRescuePressureConfirmMoveUSD",InpTrendRescuePressureConfirmMoveUSD) &&
      ValidateScaledDistanceInput("InpTrendRescueStepUSD",InpTrendRescueStepUSD);
  }

bool ValidateAndCacheFloatInputs()
  {
   if(!ValidateNonnegativeInput("InpFloatReanchorDDUSD",InpFloatReanchorDDUSD) ||
      !ValidateNonnegativeInput("InpFloatStaleMaxNetUSD",InpFloatStaleMaxNetUSD) ||
      !ValidateNonnegativeInput("InpFloatCloseBufferUSD",InpFloatCloseBufferUSD) ||
      !ValidateNonnegativeInput("InpCleanupCostBufferUSD",InpCleanupCostBufferUSD) ||
      !ValidateNonnegativeInput("InpMaxFloatLots",InpMaxFloatLots))
      return false;
   const double staleMoney=MoneyInput(InpFloatStaleMaxNetUSD);
   const double ddMoney=MoneyInput(InpFloatReanchorDDUSD);
   const double closeMoney=MoneyInput(InpFloatCloseBufferUSD);
   const double cleanupMoney=MoneyInput(InpCleanupCostBufferUSD);
   long reanchorSec=0,staleSec=0,cooldownSec=0;
   if(!ValidateNonnegativeInput("scaled InpFloatReanchorDDUSD",ddMoney) ||
      !ValidateNonnegativeInput("scaled InpFloatStaleMaxNetUSD",staleMoney) ||
      !ValidateNonnegativeInput("scaled InpFloatCloseBufferUSD",closeMoney) ||
      !ValidateNonnegativeInput("scaled InpCleanupCostBufferUSD",cleanupMoney) ||
      !CheckedIntSeconds("InpFloatReanchorHours",InpFloatReanchorHours,3600,reanchorSec) ||
      !CheckedIntSeconds("InpFloatStaleReanchorHours",InpFloatStaleReanchorHours,3600,staleSec) ||
      !CheckedIntSeconds("InpFloatStaleCooldownHours",InpFloatStaleCooldownHours,3600,cooldownSec))
      return false;
   double normalizedCap=0.0;
   if(!NormalizePositiveLotCapDown(InpMaxFloatLots,normalizedCap))
      return RejectInvalidInput("InpMaxFloatLots","positive cap below one volume step");
   g_floatStaleMaxNetMoney=staleMoney;
   g_floatReanchorSeconds=reanchorSec;
   g_floatStaleSeconds=staleSec;
   g_floatStaleCooldownSeconds=cooldownSec;
   g_normalizedFloatLotCap=normalizedCap;
   return true;
  }

bool ValidateProductionInputs()
  {
   if(!ValidateAllFiniteProductionDoubles() ||
      !ValidateNonnegativeProductionIntegers() ||
      !ValidateAllScaledProductionInputs())
      return false;
   if(!ValidateNonnegativeInput("InpEquityHardFlattenDDUSD",InpEquityHardFlattenDDUSD) ||
      !ValidateNonnegativeInput("InpEquityHardFlattenReleaseDDUSD",InpEquityHardFlattenReleaseDDUSD) ||
      !ValidateNonnegativeInput("InpBasketTakeProfitUSD",InpBasketTakeProfitUSD))
      return false;
   if(InpTesterSecPerBar<0 || (ulong)InpTesterSecPerBar>(ulong)UINT_MAX/1000ULL)
      return RejectInvalidInput("InpTesterSecPerBar","milliseconds conversion overflow");
   long markerLookback=0,markerStale=0,rescueMaxHold=0,staleTradeMinAge=0;
   if(!CheckedIntSeconds("InpStraddleProfitMarkerLookbackDays",InpStraddleProfitMarkerLookbackDays,86400,markerLookback) ||
      !CheckedIntSeconds("InpMarkerStaleHours",InpMarkerStaleHours,3600,markerStale) ||
      !CheckedIntSeconds("InpStaleTradeMinAgeMinutes",InpStaleTradeMinAgeMinutes,60,staleTradeMinAge) ||
      !CheckedDoubleSeconds("InpRescueHedgeMaxHoldHours",InpRescueHedgeMaxHoldHours,3600.0,rescueMaxHold))
      return false;
   if(InpTrendPauseLookbackBars<1 || InpManageThrottleSeconds<0 ||
      InpAvgTriggerLossUSD<0.0 || InpAvgStepUSD<0.0 || InpAvgLot<0.0 ||
      InpAvgMaxLots<0.0 || InpAvgMinMarginLevelPct<0.0)
      return RejectInvalidInput("production quantities","duration, averaging and marker values must be nonnegative");
   if(InpEquityHardFlattenDDUSD>0.0 &&
      InpEquityHardFlattenReleaseDDUSD>InpEquityHardFlattenDDUSD)
      return RejectInvalidInput("InpEquityHardFlattenReleaseDDUSD","release DD must be <= arm DD");
   if(InpMaxRetries>INT_MAX-1)
      return RejectInvalidInput("InpMaxRetries","must be <= INT_MAX-1 for bounded loop increment");
   const ulong retryMultiplier=(ulong)InpMaxRetries+1ULL;
   if(retryMultiplier==0 ||
      (InpRetryBackoffMs>0 &&
       (ulong)InpRetryBackoffMs>(ulong)UINT_MAX/retryMultiplier))
      return RejectInvalidInput("InpRetryBackoffMs","linear retry delay exceeds UINT_MAX milliseconds");
   double avgCap=0.0,trendEntryCap=0.0,oppositeCap=0.0;
   double oppositeHardCap=0.0,validationInflatedCap=0.0;
   if(!NormalizePositiveLotCapDown(InpAvgMaxLots,avgCap))
      return RejectInvalidInput("InpAvgMaxLots","positive cap below one volume step");
   if(!NormalizePositiveLotCapDown(InpTrendRescueMaxEntryLot,trendEntryCap))
      return RejectInvalidInput("InpTrendRescueMaxEntryLot","positive cap below one volume step");
   if(!NormalizePositiveLotCapDown(InpOppositeExposureMaxLots,oppositeCap))
      return RejectInvalidInput("InpOppositeExposureMaxLots","positive cap below one volume step");
   if(!NormalizePositiveLotCapDown(InpOppositeExposureHardMaxLots,oppositeHardCap))
      return RejectInvalidInput("InpOppositeExposureHardMaxLots","positive cap below one volume step");
   if(!NormalizePositiveLotCapDown(InpMarketValidationMaxInflatedLotSmallAccount,validationInflatedCap))
      return RejectInvalidInput("InpMarketValidationMaxInflatedLotSmallAccount","positive cap below one volume step");
   const uint testerMs=(uint)((ulong)InpTesterSecPerBar*1000ULL);
   if(!ValidateAndCacheFloatInputs())
      return false;
   g_normalizedAvgLotCap=avgCap;
   g_normalizedTrendRescueEntryLotCap=trendEntryCap;
   g_normalizedOppositeExposureLotCap=oppositeCap;
   g_normalizedOppositeExposureHardLotCap=oppositeHardCap;
   g_normalizedMarketValidationInflatedLotCap=validationInflatedCap;
   g_testerWaitMs=testerMs;
   g_profitMarkerLookbackSeconds=markerLookback;
   g_markerStaleSeconds=markerStale;
   g_rescueHedgeMaxHoldSeconds=rescueMaxHold;
   g_staleTradeMinAgeSeconds=staleTradeMinAge;
   g_validatedMaxRetries=InpMaxRetries;
   g_retryBackoffBaseMs=(uint)InpRetryBackoffMs;
   return true;
  }

//+------------------------------------------------------------------+
//| Expert initialization                                             |
//+------------------------------------------------------------------+
int OnInit()
  {
   g_sym = _Symbol;
   if(!g_symbol.Name(g_sym))
     {
      Print("Straddle: failed to initialize symbol info for ", g_sym);
      return(INIT_FAILED);
     }
   g_symbol.RefreshRates();
   // Set this before validation: low-priced EUR/GBP/JPY FX must use the
   // symbol-native grid even
   // when the validator has not supplied a first tick yet.
   g_lowPricedSymbol = DetectLowPricedSymbol();

   g_accountCurrency=AccountInfoString(ACCOUNT_CURRENCY);
   g_accountName=AccountInfoString(ACCOUNT_NAME);
   g_accountServer=AccountInfoString(ACCOUNT_SERVER);
   g_accountCompany=AccountInfoString(ACCOUNT_COMPANY);
   g_accountCurrencyDigits=(int)AccountInfoInteger(ACCOUNT_CURRENCY_DIGITS);
   if(g_accountCurrencyDigits<0 || g_accountCurrencyDigits>8)
      g_accountCurrencyDigits=2;
   if(!ValidateAllFiniteProductionDoubles() ||
      !ValidateNonnegativeInput("InpMoneyScaleOverride",InpMoneyScaleOverride) ||
      !DetectMoneyScale())
      return(INIT_PARAMETERS_INCORRECT);

   //--- account-type guard: dual-sided grid requires hedging (spec 2.3)
   if(!IsHedgingAccount())
     {
      Print("Straddle: refusing to run - the dual-sided BUY-STOP/SELL-STOP grid requires a ",
            "RETAIL HEDGING account. Netting/exchange accounts offset opposite fills and defeat the grid.");
      return(INIT_FAILED);
     }

   //--- input validation
   if(InpUseStaleTradeCleanup && InpStaleTradeMaxActionsPerTick <= 0)
     {
      RejectInvalidInput("InpStaleTradeMaxActionsPerTick","must be > 0 when stale cleanup is enabled");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(InpUseTrendRescueFloatingPairCleanup &&
      (InpFloatingPairCleanupProfitShare <= 0.0 ||
       InpFloatingPairCleanupMaxProfitTicketsPerTick <= 0 ||
       InpFloatingPairCleanupMaxLoserActionsPerTick <= 0))
     {
      RejectInvalidInput("floating pair cleanup","positive profit share and action caps required when enabled");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(InpUseAdaptivePriceScale &&
      (InpPriceScaleMode < PRICE_SCALE_BLEND_PERCENT_ATR ||
       InpPriceScaleMode > PRICE_SCALE_ATR_ONLY ||
       InpPriceScaleReferencePrice <= 0.0 ||
       InpPriceScalePercentWeight < 0.0 ||
       InpPriceScaleATRWeight < 0.0 ||
       (InpPriceScaleMode == PRICE_SCALE_BLEND_PERCENT_ATR &&
        InpPriceScalePercentWeight + InpPriceScaleATRWeight <= 0.0) ||
       InpPriceScaleATRPeriod < 1 ||
       InpPriceScaleReferenceATR <= 0.0 ||
       InpPriceScaleMin <= 0.0 ||
       InpPriceScaleMax < InpPriceScaleMin))
     {
      RejectInvalidInput("adaptive price scale","mode 0..2, positive references/period, nonnegative weights and ordered bounds required");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(InpUseTrendRescueNoEffectGuard &&
      (InpNoEffectMinCoverageImprovePct < 0.0 ||
       InpNoEffectMinCleanupFundingUSD < 0.0 ||
       InpNoEffectExpectedMoveATRShare < 0.0))
     {
      RejectInvalidInput("trend rescue no-effect guard","thresholds and ATR share must be nonnegative");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(InpUseTrendRescueOppositeExposureGuard && InpOppositeExposureMaxLots < 0.0)
     {
      RejectInvalidInput("InpOppositeExposureMaxLots","must be nonnegative");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(InpTesterLogSummarySec < 1)
     {
      RejectInvalidInput("InpTesterLogSummarySec","must be >= 1");
      return(INIT_PARAMETERS_INCORRECT);
     }

    if(InpLotMode < LOT_MODE_FIXED || InpLotMode > LOT_MODE_AUTO_BALANCE ||
       InpGridLevels < 1 || InpGridStepUSD <= 0.0 ||
       InpLotNear <= 0.0 || InpLotMid <= 0.0 || InpLotFar <= 0.0 ||
       InpAutoLotBaseBalanceUSD <= 0.0 || InpAutoLotNear <= 0.0 ||
       InpAutoLotMid <= 0.0 || InpAutoLotFar <= 0.0 ||
       InpAutoCycleTargetPercent <= 0.0 || InpAutoDailyProfitPercent < 0.0 ||
       InpAutoDailyLossPercent < 0.0 ||
       InpTargetUSD <= 0.0 || InpDailyProfitLimitUSD < 0.0 || InpDailyLossLimitUSD < 0.0 ||
       (int)InpDailyLossNoTradeDays < 1 || (int)InpDailyLossNoTradeDays > 4 ||
      InpPauseSeconds < 0 || InpMaxRetries < 0 || InpRetryBackoffMs < 0 ||
      InpRetrySeconds < 0 || InpLogLevel < 0 || InpMoneyScaleOverride < 0.0 ||
      InpMarketValidationSmallAccountEquityUSD < 0.0 ||
      InpMarketValidationMaxLotInflationRatio < 0.0 ||
      InpMarketValidationMaxInflatedLotSmallAccount < 0.0 ||
      InpMarketValidationMinFreeMarginAfterCheckUSD < 0.0 ||
      InpMarketValidationMinMarginLevelAfterCheckPct < 0.0 ||
      InpProfitReserveUSD < 0.0 || InpCleanupCostBufferUSD < 0.0 || InpCleanupMinLosingPositions < 1 ||
      InpRescueStatusLogSeconds < 0 || InpRescueHedgeLot <= 0.0 ||
      InpRescueHedgeCooldownSec < 0 || InpRescueMaxHedges < 0 ||
      InpRescueHedgeMinMarginLevelPct < 0.0 || InpRescueHedgeTriggerLossUSD < 0.0 ||
      InpRescueHedgeHarvestUSD < 0.0 ||
      InpTrendRescueLot <= 0.0 || InpTrendRescueStepUSD < 0.0 ||
       InpTrendRescueMaxEntries < 0 || InpTrendRescueCooldownSec < 0 ||
       InpTrendRescueMinMarginLevelPct < 0.0 || InpTrendRescueProfitTargetUSD < 0.0 ||
       InpTrendRescueExposureRatio < 0.0 || InpTrendRescueMaxEntryLot < 0.0 ||
       InpTrendRescueMoneyGapLotStepUSD <= 0.0 ||
       InpTrendRescuePressureMaxEntries < 0 ||
       InpTrendRescueTotalSafetyMaxEntries < 0 ||
       InpTrendRescuePressureGapUSD < 0.0 || InpTrendRescuePressureExposureRatio < 0.0 ||
       InpTrendRescuePressureMinExtraEntries < 0 ||
       InpTrendRescuePressureBypassStepMaxEntries < 0 ||
       InpTrendRescuePressureConfirmLookbackBars < 1 ||
       InpTrendRescuePressureConfirmMoveUSD < 0.0 ||
        InpTrendRescueMinCooldownSec < 0 || InpTrendRescueMinStepUSD < 0.0 ||
        InpTrendRescueSkipDiagGapUSD < 0.0 ||
        InpTrendRescueHarvestGapShare < 0.0 || InpTrendRescueMinAdaptiveHarvestUSD < 0.0 ||
       InpTrendRescueMaxAdaptiveHarvestUSD <= 0.0 ||
      InpTrendRescueMaxAdaptiveHarvestUSD < InpTrendRescueMinAdaptiveHarvestUSD ||
      InpTrendRescueCleanupBufferUSD < 0.0 ||
       InpTrendRescueCleanupMaxActionsPerTick < 0 ||
       InpTrendRescueEntryCleanupBufferUSD < 0.0 ||
       InpTrendRescueEntryCleanupMaxActionsPerTick < 0 ||
       InpTrendRescueEntryMinAgeSec < 0 ||
        InpPairCleanupMinProfitUSD < 0.0 || InpPairCleanupBufferUSD < 0.0 ||
        InpPairCleanupMaxActionsPerTick < 0 ||
        InpPairCleanupReserveExpirySec <= 0 ||
        InpFloatingPairCleanupProfitShare < 0.0 ||
        InpFloatingPairCleanupProfitShare > 1.0 ||
        InpFloatingPairCleanupMaxProfitTicketsPerTick < 0 ||
        InpFloatingPairCleanupMaxLoserActionsPerTick < 0 ||
        InpFloatingPairCleanupMinMarginLevelPct < 0.0 ||
        InpFloatingPairCleanupMinEquityBufferUSD < 0.0 ||
        InpEquityPressureDDUSD < 0.0 || InpEquityPressureSevereDDUSD < 0.0 ||
        (InpEquityPressureSevereDDUSD > 0.0 &&
         InpEquityPressureDDUSD > 0.0 &&
         InpEquityPressureSevereDDUSD < InpEquityPressureDDUSD) ||
        InpEquityPressureCooldownMultiplier < 1.0 ||
        InpEquityPressureStepMultiplier < 1.0 ||
        InpEquityPressureLotMultiplier < 0.0 || InpEquityPressureLotMultiplier > 1.0 ||
        InpEquityPressureMaxTrendEntries < 0 ||
        InpStaleTradeMinAgeMinutes < 0 || InpStaleTradeTriggerCount < 0 ||
        InpStaleTradeLargestLossTriggerUSD < 0.0 ||
        InpStaleTradeTotalLossTriggerUSD < 0.0 ||
        InpStaleTradeMaxLossPerTickUSD < 0.0 ||
        InpStaleTradeMaxLossPerHourUSD < 0.0 ||
        InpStaleTradeMinEquityDDUSD < 0.0 ||
        InpStaleTradeMaxActionsPerTick < 0 ||
        InpProtectedProfitFloorUSD < 0.0 || InpProtectedProfitCleanupBufferUSD < 0.0 ||
        InpProtectedProfitCleanupMaxActionsPerTick < 0 ||
        InpStuckRecoveryGapUSD < 0.0 || InpStuckRecoveryBalanceCushionUSD < 0.0 ||
        InpStuckRecoverySpendShare < 0.0 || InpStuckRecoverySpendShare > 1.0 ||
        InpStuckRecoveryMaxSpendUSD < 0.0 || InpStuckRecoveryMinEquityBufferUSD < 0.0 ||
        InpStuckRecoveryMaxActionsPerTick < 0 ||
        InpRecoveryDirectionLookbackBars < 1 || InpRecoveryDirectionMinMoveUSD < 0.0 ||
        InpRecoveryDirectionSwitchCooldownSec < 0 ||
        InpTrendRescueSkipLogThrottleSec < 0)
      {
       RejectInvalidInput("production strategy ranges","grid, sizing, recovery, cleanup, retry and safety ranges are inconsistent");
       return(INIT_PARAMETERS_INCORRECT);
      }
   if(InpDeviationPoints < 0 || InpCommissionPerLot < 0.0)
     {
      RejectInvalidInput("execution safety","InpDeviationPoints and InpCommissionPerLot must be nonnegative");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(InpTrailArmSteps < 1 || InpTrailStepN < 1 || InpTrendRescueTrailArmEntries < 1 ||
      InpTrailModifyMinSeconds < 0 || InpTrailModifyMinStepUSD < 0.0)
     {
      RejectInvalidInput("trailing","arm/step counts must be >=1 and modify thresholds nonnegative");
      return(INIT_PARAMETERS_INCORRECT);
     }

   // Complete the full raw/range/scaled/cap contract before publishing any
   // production cache or loading mutable lifecycle state.
   if(!ValidateProductionInputs())
      return(INIT_PARAMETERS_INCORRECT);

    //--- size-shape note (debug only): v0.0.11 intentionally restores the
   //    0.01 / 0.03 / 0.06 default tier shape requested by the user.
   if(InpLotFar > InpLotNear)
      Log(2, StringFormat("Straddle: tier lots increase away from center (far %.2f > near %.2f); "
                          "this is allowed and matches the v0.0.11 default sizing.",
                          InpLotFar, InpLotNear));

   //--- symbol must be tradable at all
   if((ENUM_SYMBOL_TRADE_MODE)SymbolInfoInteger(g_sym, SYMBOL_TRADE_MODE) == SYMBOL_TRADE_MODE_DISABLED)
     {
      Print("Straddle: trading is disabled for ", g_sym, " on this server.");
      return(INIT_FAILED);
     }

   //--- read every load-bearing property at runtime, never hardcode (spec 1.1 / checklist c)
   g_digits = (int)SymbolInfoInteger(g_sym, SYMBOL_DIGITS);
   // Refresh after symbol properties are available; the helper retains its
   // point/digits fallback if the validator still has no live quote.
   g_lowPricedSymbol = DetectLowPricedSymbol();
   LoadOrInitDayStartEquity(DailyDayStamp(TimeCurrent()));
   LoadPendingNextDayLot2x();
   // 1.1.36: Strategy Tester must not inherit a multi-day marker from a prior
   // visual/manual test on the same terminal login+magic+symbol.
   if(MQLInfoInteger(MQL_TESTER) || MQLInfoInteger(MQL_OPTIMIZATION))
      ClearLossCooldown();
   else
      LoadLossCooldown();
   if(ApplyLossCooldownForDay(DailyDayStamp(TimeCurrent()), true))
      LogAlways(StringFormat(
         "Straddle 1.1.36: restored daily-loss no-trade cooldown until %s",
         TimeToString(g_lossCooldownResumeDay, TIME_DATE)));
   // 1.1.36: make silent "15% ignored → $150 early stop" impossible to miss.
   if(InpUseDailyLimits && !UseDailyPercentLimits() &&
      InpAutoDailyProfitPercent > 0.0)
     {
      LogAlways(StringFormat(
         "Straddle 1.1.36: WARN daily profit path=USD (not percent). InpAutoDailyProfitPercent=%.2f%% is IGNORED; effective day profit = +%.2f (InpDailyProfitLimitUSD). For 1.1.34-style 15%% of equity set Lot Mode=Auto-Balance OR set InpDailyLimitsUsePercent=true.",
         InpAutoDailyProfitPercent,
         EffectiveDailyProfitLimitUSD()));
     }
   if(InpUseDailyLimits && UseDailyPercentLimits())
     {
      LogAlways(StringFormat(
         "Straddle 1.1.36: daily limits path=PERCENT of day-start equity (profit %.2f%% = +%.2f, loss %.2f%% = -%.2f, basis=%.2f)",
         InpAutoDailyProfitPercent,
         EffectiveDailyProfitLimitUSD(),
         InpAutoDailyLossPercent,
         EffectiveDailyLossLimitUSD(),
         AutoDailyBasisBalanceDisplay()));
     }
    LogAlways(StringFormat("Straddle 1.1.36: symbol=%s lowPrice=%s grid=%d stepRef=%.2f priceGrid=%s refPrice=%.0f lotMode=%s baseBalance=%.2f balance=%.2f lotScale=%.4f target=%.2f daily=%s openBookHard=%s dayLimitPath=%s dayProfit=+%.2f dayLoss=-%.2f nextDayLot2x=%s lossNoTradeDays=%d eqAnchor=%.2f arm=%d thr=%.0f",
                           g_sym,
                           (g_lowPricedSymbol ? "yes" : "no"),
                           EffectiveGridLevels(),
                           InpGridStepUSD,
                           (InpUsePriceProportionalGrid ? "on" : "off"),
                           InpPriceScaleReferencePrice,
                           (IsAutoLotMode() ? "auto-balance" : "fixed"),
                           InpAutoLotBaseBalanceUSD,
                           AutoLotBalanceDisplay(),
                           AutoLotScaleFactor(),
                           (IsAutoLotMode()
                            ? AutoPercentMoney(InpAutoCycleTargetPercent, AutoLotBalanceDisplay())
                            : MoneyInput(InpTargetUSD)),
                           (InpUseDailyLimits ? "on" : "off"),
                           (InpUseOpenBookHardStop ? "on" : "off"),
                           (UseDailyPercentLimits() ? "percent" : "usd"),
                           EffectiveDailyProfitLimitUSD(),
                           EffectiveDailyLossLimitUSD(),
                           (InpDailyLossNextDayLot2x ? "on" : "off"),
                           DailyLossNoTradeDays(),
                           g_dayStartEquity,
                           InpTrailArmSteps,
                           InpTrendDoubleLotLossUSD));
   double point     = SymbolInfoDouble(g_sym, SYMBOL_POINT);
   double tickSize  = TickSize();
   double tickValue = SymbolInfoDouble(g_sym, SYMBOL_TRADE_TICK_VALUE);
   double contract  = SymbolInfoDouble(g_sym, SYMBOL_TRADE_CONTRACT_SIZE);
   double vmin      = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MIN);
   double vmax      = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_MAX);
   double vstep     = SymbolInfoDouble(g_sym, SYMBOL_VOLUME_STEP);
   long   calcMode  = SymbolInfoInteger(g_sym, SYMBOL_TRADE_CALC_MODE);
   long   exeMode   = SymbolInfoInteger(g_sym, SYMBOL_TRADE_EXEMODE);
   long   swapMode  = SymbolInfoInteger(g_sym, SYMBOL_SWAP_MODE);
   long   fillMask  = SymbolInfoInteger(g_sym, SYMBOL_FILLING_MODE);
   long   stopsLvl  = SymbolInfoInteger(g_sym, SYMBOL_TRADE_STOPS_LEVEL);
   long   freezeLvl = SymbolInfoInteger(g_sym, SYMBOL_TRADE_FREEZE_LEVEL);

   double effectiveGridStep = GridStepDistance();
   double gridStepTicks = effectiveGridStep / tickSize;
   double gridStepRoundedTicks = MathRound(gridStepTicks);
   const double gridStepTickEpsilon = 1.0e-8;
   // GridStepDistance() already snaps the distance to the broker's tick.
   // Do not return INIT_PARAMETERS_INCORRECT for a symbol-specific rounding
   // difference: MetaQuotes reserves that code for an invalid input set, not
   // for a broker's tick-size representation.  The final price boundary
   // snaps every order again immediately before sending it.
   if(MathIsValidNumber(gridStepTicks) &&
      MathAbs(gridStepTicks - gridStepRoundedTicks) > gridStepTickEpsilon)
     {
      Log(1, StringFormat("Straddle: normalized grid step %.10f is represented as %.10f broker ticks on %s; continuing with tick-snapped prices",
                          effectiveGridStep, gridStepTicks, g_sym));
     }

   InitializeProtectedProfitAnchor();

   Log(1, StringFormat("Straddle init: %s digits=%d point=%s tick=%s tickValue=%.5f contract=%.2f "
                       "vol[min=%.2f max=%.2f step=%.2f] calc=%s exec=%s swap=%s fillMask=%d stops=%d freeze=%d account=%s",
                       g_sym, g_digits, DoubleToString(point, 8), DoubleToString(tickSize, 8),
                       tickValue, contract, vmin, vmax, vstep,
                       EnumToString((ENUM_SYMBOL_CALC_MODE)calcMode),
                       EnumToString((ENUM_SYMBOL_TRADE_EXECUTION)exeMode),
                       EnumToString((ENUM_SYMBOL_SWAP_MODE)swapMode),
                       (int)fillMask, (int)stopsLvl, (int)freezeLvl,
                       AccountInfoString(ACCOUNT_CURRENCY)));
   LogEffectiveMoneySettings();

   //--- 0.0.41 FIX A (hardening): the equity backstop and the rescue hedge share
   //    the g_lastRescueHedgeTime one-open-per-tick stamp. The backstop's own
   //    effective cooldown is now floored at 1s so the stamp always blocks a
   //    same-tick reverse-order backstop open. But the RESCUE hedge's cooldown
   //    gate (TryOpenRescueHedge) is disabled entirely when InpRescueHedgeCooldown
   //    Sec==0, so with FlattenDD>0 + InpUseRescueHedge + InpRescueMaxHedges>1 a
   //    rescue-hedge open and a backstop open COULD still both fire on the same
   //    tick. The CountRescueHedges<InpRescueMaxHedges cap remains the hard guard,
   //    but warn so the operator sets InpRescueHedgeCooldownSec>=1 (default 300).
   if(InpEquityHardFlattenDDUSD > 0.0 && InpUseRescueHedge &&
      InpRescueHedgeCooldownSec == 0 && InpRescueMaxHedges > 1)
      Log(1, StringFormat("Straddle: WARNING - InpRescueHedgeCooldownSec=0 with equity backstop ON (InpEquityHardFlattenDDUSD=%.2f) and InpRescueMaxHedges=%d (>1). The shared one-open-per-tick cooldown gate on the rescue hedge is DISABLED, so a rescue-hedge open and an equity-backstop open could both fire the same tick (double-stack margin spike). Set InpRescueHedgeCooldownSec>=1 (default 300) to restore the mutual exclusion.",
                          MoneyInput(InpEquityHardFlattenDDUSD), InpRescueMaxHedges));

   //--- lot-undersize warning: NormalizeLot clamps UP to SYMBOL_VOLUME_MIN,
   //    so a tier lot below the broker minimum trades LARGER than configured
   //    (e.g. 0.01 -> 0.10 on a 0.10-min broker = 10x exposure on that tier)
   if(vmin > 0.0 && (InpLotNear < vmin || InpLotMid < vmin || InpLotFar < vmin))
      Log(1, StringFormat("Straddle: WARNING - tier lot(s) below broker minimum %.2f "
                          "(near=%.2f mid=%.2f far=%.2f). Undersized lots are clamped UP to "
                          "SYMBOL_VOLUME_MIN, so REAL EXPOSURE WILL BE LARGER than configured "
                          "(e.g. 0.01 -> 0.10 on a 0.10-min broker = 10x).",
                          vmin, InpLotNear, InpLotMid, InpLotFar));

   // SYMBOL_TRADE_TICK_VALUE can be 0.0 before the first tick - it is never used
   // as a divisor in this EA; a zero value only defers the first grid build.
   if(tickValue <= 0.0)
      Log(1, "Straddle: SYMBOL_TRADE_TICK_VALUE is 0 (no tick received yet) - grid build deferred to first tick.");
   if(exeMode == SYMBOL_TRADE_EXECUTION_MARKET || exeMode == SYMBOL_TRADE_EXECUTION_EXCHANGE)
      Log(1, "Straddle: Market/Exchange execution - the deviation setting is IGNORED by the server (spec 4.1).");

   //--- CTrade setup: magic, runtime filling detection (avoids retcode 10030)
   g_trade.SetExpertMagicNumber((ulong)InpMagic);
   g_trade.SetDeviationInPoints((ulong)InpDeviationPoints);
   g_trade.LogLevel(LOG_LEVEL_NO);                      // silence CTrade journal spam;
                                                        // errors surface via our retcode handling
   LogTesterLowLogSettings();
   Log(1,"Straddle: canonical execution-mode filling resolver active");

   //--- 0.0.37 (C5): restore the persisted peak-equity high-water mark BEFORE
   //    any restart handling so a VPS restart never re-seeds peak to post-crash
   //    equity. If absent it seeds to current equity. The backstop arm/release
   //    use absolute EquityFloatingDrawdown(), so the breaker is restart-robust
   //    regardless; g_equityBackstopArmed defaults disarmed and re-arms only if
   //    DD is still past the threshold on the next tick.
   LoadPeakEquity();

   //--- restart-safe rescue hold / trend rescue: preserve "delete pendings
   //    only, hold positions" before recovery can refill or close anything.
   bool hadTrendRescueMarker = LoadTrendRescueState();
   bool hadRescueMarker = GlobalVariableCheck(RHVar());
   if(hadTrendRescueMarker || hadRescueMarker)
     {
      LoadRescueAnchorBalance();
      LoadRescueHedgeTime();
     }
   else
     {
      ClearRescueAnchorBalance();
      ClearRescueHedgeTime();
     }

   if(hadTrendRescueMarker)
     {
      int trendPositions = CountMyPositions();
      int trendPendings  = CountMyPendings();
      bool flat = (trendPositions == 0 && trendPendings == 0);
      if(trendPositions > 0)
        {
         g_trendRescueActive = true;
         g_rescueHolding = false;
         g_nextRescueTry = 0;
         GlobalVariableDel(RHVar());
         GlobalVariableDel(TDVar());
         ClearPersistedTearDownThreshold();
         Log(1, StringFormat("Straddle: restart during trend rescue detected with %d positions - deleting pendings only, holding old basket, and keeping %s-only recovery",
                             trendPositions, TrendRescueDirectionName()));
         if(!g_rescueAnchorTrusted)
            Log(1, "Straddle: trend rescue restart has open exposure but no trusted anchor balance - profit-covered reset is denied until profit is safely banked");
         LogTrendRescueStatus("restart trend rescue restore", true);
        }
      else if(flat)
        {
         ClearTrendRescueState(true);
         ClearRescueHedgeTime();
        }
      else
        {
         g_trendRescueActive = true;
         g_rescueHolding = false;
         g_nextRescueTry = 0;
         GlobalVariableDel(RHVar());
         GlobalVariableDel(TDVar());
         ClearPersistedTearDownThreshold();
         Log(1, StringFormat("Straddle: restart during trend rescue detected with %d pending orders - deleting pendings only and keeping %s-only recovery active",
                             trendPendings, TrendRescueDirectionName()));
         if(!g_rescueAnchorTrusted)
            Log(1, "Straddle: trend rescue restart has no trusted anchor balance - profit-covered reset is denied until profit is safely banked");
         LogTrendRescueStatus("restart trend rescue restore", true);
        }
     }
   else if(hadRescueMarker)
     {
      int rescuePositions = CountMyPositions();
      int rescuePendings  = CountMyPendings();
      bool flat = (rescuePositions == 0 && rescuePendings == 0);
      // Rescue markers are cleared automatically only when truly flat.
      // If matching exposure exists, even with an old marker, restore rescue
      // so the normal pending-delete hold path owns cleanup.
      if(rescuePositions > 0)
        {
         g_rescueHolding = true;
         g_nextRescueTry = 0;
         GlobalVariableDel(TDVar());
         ClearPersistedTearDownThreshold();
         Log(1, StringFormat("Straddle: restart during rescue hold detected with %d positions - deleting pendings only and holding positions",
                             rescuePositions));
         if(!g_rescueAnchorTrusted)
            Log(1, "Straddle: rescue restart has open exposure but no trusted rescue anchor balance - rescue bank is 0 until new realized profit is banked");
         LogRescueStatus("restart rescue restore", true);
        }
      else if(flat)
        {
         GlobalVariableDel(RHVar());
         ClearRescueAnchorBalance();
         ClearRescueHedgeTime();
        }
      else
        {
         g_rescueHolding = true;
         g_nextRescueTry = 0;
         GlobalVariableDel(TDVar());
         ClearPersistedTearDownThreshold();
         Log(1, StringFormat("Straddle: restart during rescue hold detected with %d pending orders - deleting pendings only and keeping rescue active",
                             rescuePendings));
         if(!g_rescueAnchorTrusted)
            Log(1, "Straddle: rescue restart has no trusted rescue anchor balance - rescue bank is 0 until a new rescue entry anchors balance");
         LogRescueStatus("restart rescue restore", true);
        }
     }

   //--- restart-safe: derive cycle state from existing positions + pendings
   //    (0.0.8: re-derives the FIXED anchor from leg comments and re-seeds
   //    per-leg peaks at entry price)
   LoadPersistedCycleStart();
   LoadPersistedCycleGridStepDistance();
   // Restore the FLOAT registry before cycle recovery so ticket membership is
   // authoritative while rebuilding the fresh-cycle anchor.
   // ticket-based membership checks (earliest-scan / anchor-candidate scan) already
   // see the floated legs. LoadFloatRegistry validates each persisted ticket still
   // has a live position and drops stale entries. The master switch gates all
   // registry mutation and Float close behavior.
   if(InpUseFloatReanchor)
      LoadFloatRegistry();
   RecoverCycleState();

   //--- 0.0.41 FIX C1: reload the persisted bounded-averaging gate state (last
   //    add price + time) so a recompile/restart mid-averaging-episode keeps the
   //    adverse-step + cooldown gates instead of treating the next add as a
   //    first-ever add. Gated on InpUseBasketAveraging so a DEFAULT-OFF startup
   //    reads NO GlobalVariable (byte-identical to 0.0.40 startup).
   if(InpUseBasketAveraging)
      LoadBasketAvgState();

   //--- resume an interrupted teardown - HONOR-WITH-FRESHNESS (0.0.5): the
   //    persisted marker distinguishes a failed-delete state (positions==0 &&
   //    pendings>0 mid-teardown) from a normal freshly-armed grid, which looks
   //    identical on disk and must NOT be torn down. The marker is DISCARDED
   //    when the instance is already flat (nothing to tear down) or its stamp
   //    is older than InpMarkerStaleHours (stale orphan). If live exposure
   //    exists but the safety threshold is missing, fail conservative into
   //    rescue hold instead of inventing a close-all threshold.
   if(GlobalVariableCheck(TDVar()))
     {
       datetime stamp = (datetime)GlobalVariableGet(TDVar());
       int teardownPositions = CountMyPositions();
       int teardownPendings  = CountMyPendings();
       bool flat = (teardownPositions == 0 && teardownPendings == 0);
       bool stale = (stamp <= 0 || (TimeCurrent() >= stamp &&
                     (long)(TimeCurrent() - stamp) > g_markerStaleSeconds));
       if(IsRescueHoldActive())
         {
          GlobalVariableDel(TDVar());   // rescue hold owns this restart state
          ClearPersistedTearDownThreshold();
          ClearPersistedBasketTearDownTag();   // 0.0.38: rescue hold owns it -> not a basket teardown
         }
       else if(flat)
         {
          GlobalVariableDel(TDVar());   // nothing to tear down -> discard
          ClearPersistedTearDownThreshold();
          ClearPersistedBasketTearDownTag();   // 0.0.38: nothing to bank -> drop the tag
         }
       else
         {
          bool thresholdLoaded = LoadPersistedTearDownThreshold();
          if(!thresholdLoaded && teardownPositions > 0)
            {
             EnterRescueHold(StringFormat("restart during teardown detected but safety threshold marker missing/unavailable, positions %d, pendings %d - deleting pendings only and holding positions; balance protected but equity risk remains",
                                          teardownPositions, teardownPendings));
            }
          else if(stale)
            {
             GlobalVariableDel(TDVar());   // stale orphan -> discard
             ClearPersistedTearDownThreshold();
             ClearPersistedBasketTearDownTag();   // 0.0.38: stale orphan -> drop the tag
            }
          else
            {
             g_tearingDown = true;
             // 0.0.38: restore the basket-teardown tag so a restart mid-basket-teardown
             // RESUMES under the floating>=+threshold guard (ProcessTearDown), never the
             // weaker generic CycleNet>=0 guard - this is what keeps the no-loss promise
             // across a VPS restart. If the tag was not persisted, this is a generic
             // teardown and the basket guard simply does not engage.
             g_basketTearDown = LoadPersistedBasketTearDownTag();
             if(!thresholdLoaded)
                g_teardownSafeThreshold = MathMax(0.0, MoneyInput(InpCleanupCostBufferUSD));
             Log(1, StringFormat("Straddle: restart during teardown detected - resuming close-out to flat%s",
                                 g_basketTearDown ? " (basket take-profit teardown - close gated on total floating >= +threshold)" : ""));
            }
         }
      }

   //--- 1-second timer as a safety net so cycle management also runs
   //    during low-tick periods (quiet sessions, rollover)
   EventSetTimer(1);
   StrUiInitialize();

   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
//| Expert deinitialization. 0.0.5: on USER-INITIATED removal (EA     |
//| removed, chart change/close, params change, template applied) the |
//| teardown marker is deleted WHEN FLAT so it cannot orphan; on a    |
//| genuine restart (recompile/terminal shutdown) it is KEPT so an    |
//| interrupted teardown still resumes after the restart.             |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   LogTesterLowLogSuppressedSummary("deinit");
   StrUiCleanup();
   EventKillTimer();
   // 0.0.40 O3: release the persisted ATR handle once on shutdown.
   if(g_atrHandle != INVALID_HANDLE)
     {
      IndicatorRelease(g_atrHandle);
      g_atrHandle = INVALID_HANDLE;
     }
   // 0.0.40 O1: OnDeinit is outside the per-tick path, so force a fresh book
   // read for the flat-removal check below (matches 0.0.39's direct scan).
   g_bookDirty = true;
   if(reason == REASON_REMOVE || reason == REASON_CHARTCHANGE || reason == REASON_PARAMETERS ||
      reason == REASON_CHARTCLOSE || reason == REASON_TEMPLATE)
     {
      if(CountMyPositions() == 0 && CountMyPendings() == 0)
        {
         GlobalVariableDel(TDVar());
          GlobalVariableDel(RHVar());
          ClearPersistedTearDownThreshold();
          ClearPersistedBasketTearDownTag();   // 0.0.38: flat removal -> drop the basket-teardown tag
          ClearPersistedCycleStart();
          ClearPersistedCycleGridStepDistance();
          ClearRescueAnchorBalance();
          ClearRescueHedgeTime();
          ClearTrendRescueState(false);
          GlobalVariableDel(PEQVar());   // 0.0.37 (C5): drop peak-equity mark on clean removal when flat
         }
      }
  }

//+------------------------------------------------------------------+
//| Tick handler - primary cycle monitor                              |
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction &trans,
                        const MqlTradeRequest &request,
                        const MqlTradeResult &result)
  {
   ReconcileTradeTransaction(trans,request,result,STR_MAX_TX_WORK);
  }

void OnTick()
  {
   // --- Backtest-only artificial per-candle throttle (debug/demo) ---------
   // Sleep() is IGNORED by the MT5 Strategy Tester, so to consume REAL
   // wall-clock time we busy-wait on GetTickCount() (which advances in real
   // time even inside the tester). Gated to a SINGLE backtest only: never in
   // live/demo trading, and never during optimization (would freeze forever).
   if(g_testerWaitMs > 0 &&
      MQLInfoInteger(MQL_TESTER) != 0 &&
      MQLInfoInteger(MQL_OPTIMIZATION) == 0)
     {
      static datetime s_throttleBar = 0;
      datetime curBar = iTime(_Symbol, Period(), 0);
      if(curBar != 0 && curBar != s_throttleBar)
        {
         s_throttleBar = curBar;
         const uint startMs = GetTickCount();
         while(GetTickCount() - startMs < g_testerWaitMs)
           {
            if(IsStopped())
               break;   // allow the user to cancel the test
           }
        }
     }

   ManageCycle();
  }

//+------------------------------------------------------------------+
//| Timer handler - low-tick safety net (1 s)                         |
//+------------------------------------------------------------------+
void OnTimer()
  {
   ManageCycle();
   StrUiRefresh(false);
  }
//+------------------------------------------------------------------+
