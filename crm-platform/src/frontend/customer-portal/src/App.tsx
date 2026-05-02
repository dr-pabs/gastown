import { lazy, Suspense } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { MsalAuthenticationTemplate } from '@azure/msal-react';
import { InteractionType } from '@azure/msal-browser';
import { NavBar } from './components/layout/NavBar';
import { Spinner } from './components/ui';
import { useIsSuperuser } from './hooks/useAuth';
import { loginRequest } from './lib/authConfig';

const MyCases = lazy(() => import('./pages/MyCases').then((m) => ({ default: m.MyCases })));
const AllCases = lazy(() => import('./pages/AllCases').then((m) => ({ default: m.AllCases })));
const NewCase = lazy(() => import('./pages/NewCase').then((m) => ({ default: m.NewCase })));
const CaseDetail = lazy(() => import('./pages/CaseDetail').then((m) => ({ default: m.CaseDetail })));

function AllCasesGuard() {
  const isSuperuser = useIsSuperuser();
  if (!isSuperuser) return <Navigate to="/cases/my" replace />;
  return <AllCases />;
}

function AppRoutes() {
  return (
    <div className="min-h-screen bg-gray-50">
      <NavBar />
      <main>
        <Suspense
          fallback={
            <div className="flex h-64 items-center justify-center">
              <Spinner size="lg" />
            </div>
          }
        >
          <Routes>
            <Route path="/" element={<Navigate to="/cases/my" replace />} />
            <Route path="/cases/my" element={<MyCases />} />
            <Route path="/cases/all" element={<AllCasesGuard />} />
            <Route path="/cases/new" element={<NewCase />} />
            <Route path="/cases/:id" element={<CaseDetail />} />
            <Route path="*" element={<Navigate to="/cases/my" replace />} />
          </Routes>
        </Suspense>
      </main>
    </div>
  );
}

export function App() {
  return (
    <MsalAuthenticationTemplate
      interactionType={InteractionType.Redirect}
      authenticationRequest={loginRequest}
    >
      <AppRoutes />
    </MsalAuthenticationTemplate>
  );
}
