# Privacy Policy

This Policy is a translation of the Korean original. If the two differ, the Korean version prevails.

{{name}} (the "Company") sets out this Privacy Policy under the Personal Information Protection Act of Korea to protect users' personal information and to handle related concerns promptly.

## Article 1 (Personal Information Processed and Purposes)
The Company processes only the minimum personal information needed for the following purposes.

| Purpose | Items |
|---|---|
| Account creation and login | Steam ID (17 digits), Steam ID of the Family Sharing owner, account identifier, account creation time, login times |
| Keeping users logged in | Hash of the refresh token, install ID, device identifier (hashed) |
| Concurrent session control and fraud prevention | Connection IP, device identifier (hashed), install ID, client version, session state (character, map) |
| Game progress | Character data, level, experience, inventory, records of currency and item changes |
| Chat and reports | Chat messages (with banned words masked), report details and chat lines copied as evidence, block lists |
| Restrictions on use | Sanction records, economy hold records |
| Payments | Steam ID, order amount and currency, Steam country, IP and device identifier (hashed) at payment, payment status |
| Error diagnosis | Client version, platform, error message (up to 500 characters), error location data (up to 2,000 characters) |
| Server operation and security | Web server access logs (connection IP, request time and path, response code, client information), server system logs |
| Records of consent to terms | Documents and versions agreed to, time of consent |
| Party dungeon result checks | Device identifier (hashed) and install ID of members in the same run |
| Play time selection | Allowed play hours, material confirming the relationship between the requester and the user (when sent by email) |

The Company does not collect names, dates of birth, phone numbers, addresses or email addresses. If a user sends an email for an inquiry or appeal, the Company processes the email address and its content only as needed to handle it.

Device identifiers are never stored as-is. The value hashed once on the user's device is hashed again by the server with a secret key before storage.

## Article 2 (Retention Periods)
The Company destroys personal information without delay when the following periods end.

| Item | Retention period |
|---|---|
| Chat messages | 7 days from sending |
| Chat lines copied as report evidence | 180 days after the report is closed |
| Login records (IP, device identifier, install ID, client version) | 90 days |
| Per-account device and IP aggregates | 180 days from the last time observed |
| Session state records | 7 days after the session ends |
| Hourly play time and earnings aggregates | 35 days |
| Hashes of expired or revoked refresh tokens | 30 days |
| Block lists | 90 days after unblocking |
| Friend relations | 90 days after the relation ends |
| Error reports | 14 days |
| Web server access logs | 14 days |
| Server system logs | Up to 30 days |
| Device identifier and install ID of party run members | 30 days after the run ends |
| Fraud detection records | 30 days (180 days for severe records) |
| Request replay records | 7 days (24 hours for some requests) |
| IP and device identifier at payment | 180 days |
| Payment records, currency and item ledgers | While the account exists; after withdrawal, 5 years from the last record (Korean Act on Consumer Protection in Electronic Commerce) |
| Sanction records, report records, records of consent to terms | While the account exists; after withdrawal, kept for the same 5-year period |
| Play time selection settings | Until removed or until withdrawal |

## Article 3 (Processing on Withdrawal)
1. On a withdrawal request, all login tokens are revoked and the user is disconnected immediately. Character names are changed to values other users cannot recognise.
2. The withdrawal can be cancelled within 30 days of the request.
3. If a sanction, a pending report or a pending payment is in progress, anonymisation may be deferred until it is resolved, for up to 180 days.
4. Payment records and currency ledgers that must be kept by law, and the sanction and report records linked to them, are kept separately for the period in Article 2 and then destroyed.
5. To prevent re-registration that evades a sanction, if a user withdraws while under restriction, an irreversibly transformed value of the Steam ID is kept for up to 3 years.

After 30 days, the following processing takes place.

- **Deleted**: login data such as the Steam ID, login tokens, login records, error reports, device and IP aggregates, session state, play aggregates, request records, chat sent or received, and blocks the user placed
- **Anonymised**: the user's name in other users' block lists and in reports, device data in party records, and access times of the account and characters

## Article 4 (Destruction Procedure and Method)
1. Information whose retention period has ended is destroyed automatically at a set time each day.
2. Electronic files are deleted by methods that prevent recovery. No paper documents are produced.
3. Backups are stored encrypted, and information in backups is destroyed when the backup retention period ends.

## Article 5 (Provision to Third Parties)
The Company does not provide users' personal information to third parties, except where required by law or requested by investigative authorities following procedures set by law.

## Article 6 (Outsourcing and Overseas Transfer)
To provide the Service, the Company entrusts processing to, or transfers data overseas to, the following.

| Recipient | Country | Purpose | Items | Timing and method |
|---|---|---|---|---|
| Valve Corporation (Steam) | United States | Login authentication, payment processing | Steam ID, payment data | Sent over the network at login and payment |
| Google LLC (Google Cloud, server hosting) | Stored in Korea (Seoul region) | Running the game servers | All items in Article 1 | Continuously while the Service is used |
| Supabase Inc. (database, AWS Seoul region) | Stored in Korea (Seoul region) | Data storage | All items in Article 1 | Continuously while the Service is used |

The servers and database are processed by these contractors with data stored in Korea (Seoul region); the only overseas transfer is to Valve Corporation. If the storage location changes, this table will be updated. Retention periods are as in Article 2. If a user refuses overseas transfer, Steam login and payment are not possible and the Service cannot be used.

## Article 7 (Information Stored on the User's Device)
The game stores the following on the user's device. No cookies are used.

- Refresh token for staying logged in (encrypted with an operating system feature on Windows)
- Server address
- Install ID
- Settings (volume, resolution, language, key bindings, chat friend and block names)
- Offline save files

Uninstalling the game or deleting its save folder removes this information.

## Article 8 (User Rights)
1. Users may at any time request access to, correction, deletion or suspension of processing of their personal information.
2. Deletion is available through the in-game withdrawal feature. Other requests may be sent to {{privacy_officer_email}}; the Company responds within 10 days.
3. Information that must be kept by law cannot be deleted before its retention period ends, even on request.

## Article 9 (Children Under 14)
The Company does not accept registration from children under 14. If it learns that a child under 14 has registered, it destroys that account's personal information without delay.

## Article 10 (Security Measures)
- Only hashes of login tokens are stored. Development passwords are stored hashed with argon2id.
- Device identifiers are hashed with a secret key; the original values are not stored.
- Database backups are stored encrypted.
- Administrative tools separate permissions by role (viewer, operator, owner), and every administrator action is recorded in an audit log.
- Communication between the server and users is encrypted (HTTPS).

## Article 11 (Privacy Officer)

| Item | Details |
|---|---|
| Privacy officer | {{privacy_officer}} |
| Email | {{privacy_officer_email}} |

## Article 12 (Remedies)
For reports or consultations about privacy infringement, users may contact:

- Personal Information Infringement Report Center: 118 (privacy.kisa.or.kr)
- Personal Information Dispute Mediation Committee: 1833-6972 (www.kopico.go.kr)
- Supreme Prosecutors' Office: 1301 (www.spo.go.kr)
- Korean National Police Agency: 182 (ecrm.police.go.kr)

## Article 13 (Changes)
Changes to this Policy are announced in-game and on the web page at least 7 days before they take effect, and at least 30 days in advance for changes important to users' rights.

This Policy takes effect on {{effective_date}}.
