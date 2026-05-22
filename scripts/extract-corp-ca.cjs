/**
 * extract-corp-ca.cjs
 *
 * On a corporate network that intercepts TLS (proxy / firewall), Node rejects
 * npm/pnpm/expo downloads with UNABLE_TO_VERIFY_LEAF_SIGNATURE because the
 * proxy's root CA isn't in Node's trust store. This helper connects to the npm
 * registry, captures the certificate chain the proxy presents, and writes it to
 * a PEM file you can point Node at via `cafile` / NODE_EXTRA_CA_CERTS.
 *
 * Usage:  node scripts/extract-corp-ca.cjs <output-path>
 * Example: node scripts/extract-corp-ca.cjs "C:\\Users\\you\\corp-ca.pem"
 */
const tls = require('tls');
const fs = require('fs');

const host = process.env.CA_PROBE_HOST || 'registry.npmjs.org';
const outPath = process.argv[2] || 'corp-ca.pem';

const socket = tls.connect(
  { host, port: 443, servername: host, rejectUnauthorized: false, timeout: 15000 },
  () => {
    let cert = socket.getPeerCertificate(true);
    const pems = [];
    const seen = new Set();
    while (cert && cert.raw && !seen.has(cert.fingerprint256)) {
      seen.add(cert.fingerprint256);
      const b64 = cert.raw.toString('base64').match(/.{1,64}/g).join('\n');
      const cn = (cert.subject && cert.subject.CN) || '(no CN)';
      const issuer = (cert.issuer && cert.issuer.CN) || '(no issuer CN)';
      pems.push(`# Subject: ${cn}\n# Issuer:  ${issuer}\n-----BEGIN CERTIFICATE-----\n${b64}\n-----END CERTIFICATE-----`);
      const next = cert.issuerCertificate;
      if (next && next.fingerprint256 && next.fingerprint256 !== cert.fingerprint256) cert = next;
      else break;
    }
    fs.writeFileSync(outPath, pems.join('\n') + '\n');
    console.log(`Wrote ${pems.length} certificate(s) to ${outPath}`);
    socket.end();
    process.exit(0);
  }
);
socket.on('error', (e) => {
  console.error('ERROR:', e.code || '', e.message);
  process.exit(1);
});
socket.on('timeout', () => {
  console.error('ERROR: connection timed out');
  process.exit(1);
});
