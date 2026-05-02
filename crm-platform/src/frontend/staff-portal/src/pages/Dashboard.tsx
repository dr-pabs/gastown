import { useTranslation } from 'react-i18next';
import { useMsal } from '@azure/msal-react';
import { useLeads } from '../hooks/useLeads';
import { useCases } from '../hooks/useCases';
import { useOpportunities } from '../hooks/useOpportunities';
import { useNotifications } from '../hooks/useNotifications';
import { Spinner } from '../components/ui';
import { Link } from 'react-router-dom';
import type { Lead, Case, Opportunity } from '../types';

function StatCard({ title, value, to }: { title: string; value: number | string; to: string }) {
  return (
    <Link
      to={to}
      className="block rounded-lg border bg-white p-6 shadow-sm hover:shadow-md transition-shadow"
    >
      <p className="text-sm font-medium text-gray-500">{title}</p>
      <p className="mt-2 text-3xl font-bold text-gray-900">{value}</p>
    </Link>
  );
}

export function Dashboard() {
  const { t } = useTranslation();
  const { accounts } = useMsal();
  const displayName = accounts[0]?.name ?? '';

  const { data: leadsData, isLoading: leadsLoading } = useLeads({ page: 1, pageSize: 5 });
  const { data: casesData, isLoading: casesLoading } = useCases({ page: 1, pageSize: 5 });
  const { data: oppsData, isLoading: oppsLoading } = useOpportunities({ page: 1, pageSize: 5 });
  const { data: notifData } = useNotifications({ page: 1, pageSize: 5 });

  const isLoading = leadsLoading || casesLoading || oppsLoading;

  return (
    <div className="p-6">
      <h1 className="mb-1 text-2xl font-bold text-gray-900">
        {t('dashboard.title')}
      </h1>
      {displayName && (
        <p className="mb-6 text-gray-500">
          {t('dashboard.welcomeBack', { name: displayName })}
        </p>
      )}

      {isLoading ? (
        <div className="flex justify-center py-16">
          <Spinner size="lg" />
        </div>
      ) : (
        <>
          {/* Stats */}
          <div className="mb-8 grid grid-cols-2 gap-4 sm:grid-cols-4">
            <StatCard
              title={t('dashboard.openLeads')}
              value={leadsData?.totalCount ?? 0}
              to="/leads"
            />
            <StatCard
              title={t('dashboard.openCases')}
              value={casesData?.totalCount ?? 0}
              to="/cases"
            />
            <StatCard
              title={t('dashboard.opportunities')}
              value={oppsData?.totalCount ?? 0}
              to="/opportunities"
            />
            <StatCard
              title={t('notifications.unread')}
              value={notifData?.items.filter((n) => !n.isRead).length ?? 0}
              to="/notifications"
            />
          </div>

          {/* Recent leads */}
          <section className="mb-8">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-gray-800">{t('leads.title')}</h2>
              <Link to="/leads" className="text-sm text-primary-600 hover:underline">
                {t('common.actions')} →
              </Link>
            </div>
            <div className="space-y-2">
              {(leadsData?.items ?? []).slice(0, 5).map((lead: Lead) => (
                <Link
                  key={lead.id}
                  to={`/leads/${lead.id}`}
                  className="flex items-center justify-between rounded-md border bg-white px-4 py-3 hover:bg-gray-50"
                >
                  <span className="text-sm font-medium">{lead.firstName} {lead.lastName}</span>
                  <span className="text-xs text-gray-400">{lead.stage}</span>
                </Link>
              ))}
            </div>
          </section>

          {/* Recent cases */}
          <section>
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-lg font-semibold text-gray-800">{t('cases.title')}</h2>
              <Link to="/cases" className="text-sm text-primary-600 hover:underline">
                {t('common.actions')} →
              </Link>
            </div>
            <div className="space-y-2">
              {(casesData?.items ?? []).slice(0, 5).map((c: Case) => (
                <Link
                  key={c.id}
                  to={`/cases/${c.id}`}
                  className="flex items-center justify-between rounded-md border bg-white px-4 py-3 hover:bg-gray-50"
                >
                  <span className="text-sm font-medium">{c.subject}</span>
                  <span className="text-xs text-gray-400">{c.status}</span>
                </Link>
              ))}
            </div>
          </section>
        </>
      )}
    </div>
  );
}
