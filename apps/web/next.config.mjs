/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  // Consume the shared workspace package as TypeScript source.
  transpilePackages: ['@nbc/shared'],
  experimental: {
    typedRoutes: false,
  },
};

export default nextConfig;
