import { useState } from 'react';
import type { CloudPlanView, CloudRunnerView } from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import type { AppView } from '../../app/navigation';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import './cloud.css';

/** Grid template shared by the runners table head and every runner row. */
const runnerColumns = '1.8fr 1.2fr 1fr 1fr 1fr 120px';

type BillingPeriod = 'MONTHLY' | 'YEARLY';

const billingOptions: readonly { readonly period: BillingPeriod; readonly label: string }[] = [
  { period: 'MONTHLY', label: 'Monthly' },
  { period: 'YEARLY', label: 'Yearly' },
];

export interface CloudPageProps {
  readonly onNavigate: (view: AppView, strategyId?: string) => void;
  /** Starts the purchase flow for a plan. Falls back to the bots page when absent. */
  readonly onSelectPlan?: (plan: CloudPlanView, billingPeriod: BillingPeriod) => void;
  /** Opens the management surface for a single runner. */
  readonly onManageRunner?: (runner: CloudRunnerView) => void;
}

function formatCents(cents: number, currency: string): string {
  const amount = cents / 100;
  const fractionDigits = cents % 100 === 0 ? 0 : 2;
  try {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency,
      minimumFractionDigits: fractionDigits,
      maximumFractionDigits: fractionDigits,
    }).format(amount);
  } catch {
    return `${amount.toFixed(fractionDigits)} ${currency}`;
  }
}

const invoiceDateFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  day: '2-digit',
  month: 'short',
  year: 'numeric',
});

function formatInvoiceDate(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : invoiceDateFormat.format(parsed);
}

const percentFormat = new Intl.NumberFormat('en-GB', { maximumFractionDigits: 1 });

interface InvoiceSummary {
  readonly date: string;
  readonly total: string;
}

/**
 * The next invoice line beside "Active runners".
 *
 * Returns null unless real runners carry a `nextInvoiceAt` — the design's sample
 * figure must never stand in for one.
 */
function summariseInvoice(runners: readonly CloudRunnerView[]): InvoiceSummary | null {
  const dated = runners.filter((runner) => runner.nextInvoiceAt !== null);
  const first = dated[0];
  if (first === undefined || first.nextInvoiceAt === null) {
    return null;
  }

  let earliest = first.nextInvoiceAt;
  for (const runner of dated) {
    if (runner.nextInvoiceAt !== null && runner.nextInvoiceAt < earliest) {
      earliest = runner.nextInvoiceAt;
    }
  }

  const currency = first.currency;
  const billable = runners.filter((runner) => runner.currency === currency);
  const totalCents = billable.reduce((sum, runner) => sum + runner.monthlyPriceCents, 0);

  return { date: formatInvoiceDate(earliest), total: formatCents(totalCents, currency) };
}

export function CloudPage({ onNavigate, onSelectPlan, onManageRunner }: CloudPageProps) {
  const client = useControlPlaneClient();
  const plans = useResource((signal) => client.getCloudPlans(signal), [client]);
  const runners = useResource((signal) => client.getCloudRunners(signal), [client]);
  const regions = useResource((signal) => client.getCloudRegions(signal), [client]);

  const [billingPeriod, setBillingPeriod] = useState<BillingPeriod>('MONTHLY');

  const runnerList = runners.state.status === 'ready' ? runners.state.value : [];
  const invoice = runnerList.length === 0 ? null : summariseInvoice(runnerList);

  return (
    <div className="page">
      <div className="page-head cloud-head">
        <div>
          <h1 className="page-title">Cloud runners</h1>
          <p className="page-subtitle">
            One runner hosts one bot, 24/7, on our servers. Running locally stays free forever.
          </p>
        </div>
        <div className="cloud-billing" role="group" aria-label="Billing period">
          {billingOptions.map((option) => (
            <button
              key={option.period}
              type="button"
              className={`chip cloud-billing__option${billingPeriod === option.period ? ' chip--active' : ''}`}
              aria-pressed={billingPeriod === option.period}
              onClick={() => setBillingPeriod(option.period)}
            >
              {option.label}
            </button>
          ))}
        </div>
      </div>

      <div className="cloud-plans">
        {plans.state.status === 'loading'
          ? Array.from({ length: 3 }, (_unused, index) => (
            <div key={index} className="cloud-plan">
              <div className="skeleton cloud-plan__skeleton cloud-plan__skeleton--title" />
              <div className="skeleton cloud-plan__skeleton cloud-plan__skeleton--price" />
              <div className="skeleton cloud-plan__skeleton" />
              <div className="skeleton cloud-plan__skeleton" />
            </div>
          ))
          : null}

        {plans.state.status === 'unauthorized' ? (
          <p className="empty-state cloud-plans__state">Sign in again to see cloud pricing.</p>
        ) : null}

        {plans.state.status === 'error' ? (
          <div className="empty-state cloud-plans__state">
            <p>Cloud plans could not be loaded. {userFacingProblem(plans.state.error)}</p>
            <button type="button" className="btn btn--row" onClick={plans.reload}>
              Try again
            </button>
          </div>
        ) : null}

        {plans.state.status === 'ready' && plans.state.value.length === 0 ? (
          <p className="empty-state cloud-plans__state">
            No cloud plans are published yet. Local execution stays free in the meantime.
          </p>
        ) : null}

        {plans.state.status === 'ready'
          ? plans.state.value.map((plan) => {
            const cents = billingPeriod === 'YEARLY' ? plan.priceYearlyCents : plan.priceMonthlyCents;
            return (
              <div
                key={plan.id}
                className={`cloud-plan${plan.highlighted ? ' cloud-plan--highlighted' : ''}`}
              >
                <div className="cloud-plan__head">
                  <h2 className="cloud-plan__name">{plan.name}</h2>
                  {plan.tag === null ? null : <span className="badge badge--accent">{plan.tag}</span>}
                </div>
                <div className="cloud-plan__price-row">
                  <div className="cloud-plan__price mono">{formatCents(cents, plan.currency)}</div>
                  <div className="cloud-plan__unit">{plan.unit}</div>
                </div>
                <p className="cloud-plan__period">
                  {billingPeriod === 'YEARLY' ? 'billed once a year' : 'billed every month'}
                </p>
                <p className="cloud-plan__blurb">{plan.blurb}</p>
                <div className="cloud-plan__divider" />
                <ul className="cloud-plan__features">
                  {plan.features.map((feature) => (
                    <li key={feature} className="cloud-plan__feature">
                      <Icon name="check" size={13} className="cloud-plan__check" />
                      <span>{feature}</span>
                    </li>
                  ))}
                </ul>
                <button
                  type="button"
                  className={`btn cloud-plan__cta ${plan.highlighted ? 'btn--primary' : 'btn--secondary'}`}
                  onClick={() => {
                    if (onSelectPlan === undefined) {
                      onNavigate('bots');
                      return;
                    }
                    onSelectPlan(plan, billingPeriod);
                  }}
                >
                  {plan.ctaLabel}
                </button>
              </div>
            );
          })
          : null}
      </div>

      <div className="cloud-section-head">
        <h2 className="section-title">Active runners</h2>
        {invoice === null ? null : (
          <span className="cloud-invoice mono">
            next invoice {invoice.date} · {invoice.total}
          </span>
        )}
      </div>

      <div className="panel">
        <div className="table">
          <div className="table__head" style={{ gridTemplateColumns: runnerColumns }}>
            <div>Bot</div>
            <div>Region</div>
            <div>Uptime 30d</div>
            <div>Latency</div>
            <div>Billing</div>
            <div />
          </div>

          {runners.state.status === 'loading'
            ? Array.from({ length: 2 }, (_unused, index) => (
              <div key={index} className="table__row" style={{ gridTemplateColumns: runnerColumns }}>
                <div className="skeleton cloud-skeleton" />
                <div className="skeleton cloud-skeleton" />
                <div className="skeleton cloud-skeleton" />
                <div className="skeleton cloud-skeleton" />
                <div className="skeleton cloud-skeleton" />
                <div className="skeleton cloud-skeleton" />
              </div>
            ))
            : null}

          {runners.state.status === 'unauthorized' ? (
            <p className="empty-state">Sign in again to see your cloud runners.</p>
          ) : null}

          {runners.state.status === 'error' ? (
            <div className="empty-state">
              <p>Cloud runners could not be loaded. {userFacingProblem(runners.state.error)}</p>
              <button type="button" className="btn btn--row" onClick={runners.reload}>
                Try again
              </button>
            </div>
          ) : null}

          {runners.state.status === 'ready' && runnerList.length === 0 ? (
            <p className="empty-state">
              No runners yet. Nothing is being billed, and every bot you have runs on this machine only.
            </p>
          ) : null}

          {runners.state.status === 'ready'
            ? runnerList.map((runner) => (
              <div key={runner.id} className="table__row" style={{ gridTemplateColumns: runnerColumns }}>
                <div className="cloud-runner__bot">
                  <span className="dot dot--cloud" />
                  {runner.botName}
                </div>
                <div className="cloud-cell mono">
                  {runner.regionLabel} · {runner.regionCode}
                </div>
                <div className="cloud-uptime mono">{percentFormat.format(runner.uptime30dPercent)}%</div>
                <div className="cloud-cell mono">{runner.latencyMs} ms</div>
                <div className="cloud-cell mono">
                  {formatCents(runner.monthlyPriceCents, runner.currency)} / mo
                </div>
                <div className="cloud-runner__actions">
                  {onManageRunner === undefined ? (
                    <button
                      type="button"
                      className="btn btn--row"
                      disabled
                      title="Runner management is not available in this build."
                    >
                      Manage
                    </button>
                  ) : (
                    <button type="button" className="btn btn--row" onClick={() => onManageRunner(runner)}>
                      Manage
                    </button>
                  )}
                </div>
              </div>
            ))
            : null}
        </div>
      </div>

      <div className="cloud-explainer">
        <div className="cloud-explainer__copy">
          <h3 className="cloud-explainer__title">How a runner works</h3>
          <p className="cloud-explainer__body">
            You pick a bot and a region near your broker. We start a dedicated runner, hand it your account
            credentials over the bridge, and it executes the same strategy file you tested locally. Stop it any
            time — billing is monthly, per bot.
          </p>
        </div>
        <div className="cloud-explainer__regions">
          <p className="cloud-explainer__regions-label">Regions available</p>

          {regions.state.status === 'loading' ? (
            <div className="cloud-region-list">
              {Array.from({ length: 5 }, (_unused, index) => (
                <div key={index} className="skeleton cloud-region-skeleton" />
              ))}
            </div>
          ) : null}

          {regions.state.status === 'unauthorized' ? (
            <p className="cloud-explainer__regions-empty">Sign in again to see the region list.</p>
          ) : null}

          {regions.state.status === 'error' ? (
            <p className="cloud-explainer__regions-empty">The region list could not be loaded.</p>
          ) : null}

          {regions.state.status === 'ready' && regions.state.value.length === 0 ? (
            <p className="cloud-explainer__regions-empty">No regions have been published yet.</p>
          ) : null}

          {regions.state.status === 'ready' && regions.state.value.length > 0 ? (
            <div className="cloud-region-list">
              {regions.state.value.map((region) => (
                <span key={region.code} className="cloud-region mono">
                  {region.label} · {region.code}
                </span>
              ))}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
