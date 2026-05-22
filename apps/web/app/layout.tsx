import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'NBC PBRMS',
  description: 'NBC Production Booking & Roster Management System',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
