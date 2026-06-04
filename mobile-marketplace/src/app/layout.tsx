import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Syncro Mobile Store",
  description: "The premium mobile app marketplace for Syncro Desktop.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body style={{ margin: 0 }}>{children}</body>
    </html>
  );
}
