import { Suspense, lazy } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { MsalAuthenticationTemplate } from '@azure/msal-react';
import { InteractionType } from '@azure/msal-browser';
import { loginRequest } from './lib/authConfig';
import { Sidebar } from './components/layout/Sidebar';
import { TopBar } from './components/layout/TopBar';
import { Spinner } from './components/ui';

// Lazy-load every page to keep initial bundle small
const Dashboard = lazy(() => import('./pages/Dashboard').then((m) => ({ default: m.Dashboard })));
const Leads = lazy(() => import('./pages/Leads').then((m) => ({ default: m.Leads })));
const LeadDetail = lazy(() => import('./pages/LeadDetail').then((m) => ({ default: m.LeadDetail })));
const Contacts = lazy(() => import('./pages/Contacts').then((m) => ({ default: m.Contacts })));
const ContactDetail = lazy(() => import('./pages/ContactDetail').then((m) => ({ default: m.ContactDetail })));
const Accounts = lazy(() => import('./pages/Accounts').then((m) => ({ default: m.Accounts })));
const AccountDetail = lazy(() => import('./pages/AccountDetail').then((m) => ({ default: m.AccountDetail })));
const Opportunities = lazy(() => import('./pages/Opportunities').then((m) => ({ default: m.Opportunities })));
const OpportunityDetail = lazy(() => import('./pages/OpportunityDetail').then((m) => ({ default: m.OpportunityDetail })));
const Cases = lazy(() => import('./pages/Cases').then((m) => ({ default: m.Cases })));
const CaseDetail = lazy(() => import('./pages/CaseDetail').then((m) => ({ default: m.CaseDetail })));
const Campaigns = lazy(() => import('./pages/Campaigns').then((m) => ({ default: m.Campaigns })));
const Journeys = lazy(() => import('./pages/Journeys').then((m) => ({ default: m.Journeys })));
const Analytics = lazy(() => import('./pages/Analytics').then((m) => ({ default: m.Analytics })));
const Notifications = lazy(() => import('./pages/Notifications').then((m) => ({ default: m.Notifications })));
const PromptTemplates = lazy(() => import('./pages/Settings/PromptTemplates').then((m) => ({ default: m.PromptTemplates })));
const Connectors = lazy(() => import('./pages/Settings/Connectors').then((m) => ({ default: m.Connectors })));

function PageSpinner() {
  return (
    <div className="flex h-64 items-center justify-center">
      <Spinner size="lg" />
    </div>
  );
}

function AppLayout() {
  return (
    <div className="flex h-screen overflow-hidden bg-gray-50">
      <Sidebar />
      <div className="flex flex-1 flex-col overflow-hidden">
        <TopBar />
        <main className="flex-1 overflow-y-auto">
          <Suspense fallback={<PageSpinner />}>
            <Routes>
              <Route index element={<Dashboard />} />
              <Route path="leads" element={<Leads />} />
              <Route path="leads/:id" element={<LeadDetail />} />
              <Route path="contacts" element={<Contacts />} />
              <Route path="contacts/:id" element={<ContactDetail />} />
              <Route path="accounts" element={<Accounts />} />
              <Route path="accounts/:id" element={<AccountDetail />} />
              <Route path="opportunities" element={<Opportunities />} />
              <Route path="opportunities/:id" element={<OpportunityDetail />} />
              <Route path="cases" element={<Cases />} />
              <Route path="cases/:id" element={<CaseDetail />} />
              <Route path="campaigns" element={<Campaigns />} />
              <Route path="journeys" element={<Journeys />} />
              <Route path="analytics" element={<Analytics />} />
              <Route path="notifications" element={<Notifications />} />
              <Route path="settings/prompt-templates" element={<PromptTemplates />} />
              <Route path="settings/connectors" element={<Connectors />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </Suspense>
        </main>
      </div>
    </div>
  );
}

export function App() {
  return (
    <MsalAuthenticationTemplate
      interactionType={InteractionType.Redirect}
      authenticationRequest={loginRequest}
    >
      <Routes>
        <Route path="/*" element={<AppLayout />} />
      </Routes>
    </MsalAuthenticationTemplate>
  );
}
