export const Alert = ({ kind = 'error', children }: { kind?: 'error' | 'success' | 'info'; children: React.ReactNode }) => {
  const colors = { error: 'bg-red-50 text-red-800', success: 'bg-green-50 text-green-800', info: 'bg-blue-50 text-blue-800' };
  return <div role={kind === 'error' ? 'alert' : 'status'} className={`mb-4 rounded-md p-3 text-sm ${colors[kind]}`}>{children}</div>;
};
