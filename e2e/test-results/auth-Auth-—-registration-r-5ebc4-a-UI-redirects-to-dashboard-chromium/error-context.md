# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: auth.spec.ts >> Auth — registration >> register new org via UI redirects to dashboard
- Location: tests/auth.spec.ts:132:7

# Error details

```
TimeoutError: page.waitForURL: Timeout 15000ms exceeded.
=========================== logs ===========================
waiting for navigation to "**/dashboard" until "load"
============================================================
```

# Page snapshot

```yaml
- generic [ref=e5]:
  - generic [ref=e7]:
    - generic [ref=e8]: Create Your Account
    - generic [ref=e9]: 14 days free, no credit card required
  - generic [ref=e11]:
    - generic [ref=e12]: Registration failed (0). Please try again.
    - generic [ref=e15] [cursor=pointer]:
      - generic [ref=e16]:
        - text: I am registering as
        - generic [ref=e17]: "*"
      - img [ref=e19]: manage_accounts
      - combobox "I am registering as Org Admin — I manage a senior living organization" [ref=e21]:
        - generic [ref=e22]:
          - generic [ref=e24]: Org Admin — I manage a senior living organization
          - img [ref=e27]
    - separator [ref=e30]
    - paragraph [ref=e31]:
      - img [ref=e32]: business
      - text: Your Organization & Main Branch
    - generic [ref=e35]:
      - generic [ref=e36]:
        - text: Organization Name
        - generic [ref=e37]: "*"
      - img [ref=e39]: corporate_fare
      - textbox "Organization Name" [ref=e41]:
        - /placeholder: e.g., Sunrise Senior Living Group
        - text: E2E Org 1782938995943
    - generic [ref=e45]:
      - generic [ref=e46]:
        - text: Main Branch / Facility Name
        - generic [ref=e47]: "*"
      - img [ref=e49]: location_city
      - textbox "Main Branch / Facility Name" [ref=e51]:
        - /placeholder: e.g., Downtown Branch
        - text: E2E Facility 1782938995943
    - generic [ref=e55]:
      - generic [ref=e56]:
        - text: Branch Address
        - generic [ref=e57]: "*"
      - img [ref=e59]: place
      - textbox "Branch Address" [ref=e61]:
        - /placeholder: e.g., 123 Main St, Austin, TX 78701
        - text: 1 Test Ave, Austin TX 78701
    - separator [ref=e63]
    - paragraph [ref=e64]:
      - img [ref=e65]: person
      - text: Your Details
    - generic [ref=e66]:
      - generic [ref=e69]:
        - generic [ref=e70]:
          - text: First Name
          - generic [ref=e71]: "*"
        - textbox "First Name" [ref=e73]: E2E
      - generic [ref=e77]:
        - generic [ref=e78]:
          - text: Last Name
          - generic [ref=e79]: "*"
        - textbox "Last Name" [ref=e81]: User
    - generic [ref=e85]:
      - generic [ref=e86]:
        - text: Work Email
        - generic [ref=e87]: "*"
      - img [ref=e89]: email
      - textbox "Work Email" [ref=e91]: e2e-1782938995943@kincare-test.invalid
    - generic [ref=e93]:
      - generic [ref=e95]:
        - generic [ref=e96]:
          - text: Password
          - generic [ref=e97]: "*"
        - img [ref=e99]: lock
        - textbox "Password" [ref=e101]: E2ePassw0rd!
        - button "Toggle password visibility" [ref=e103] [cursor=pointer]:
          - img [ref=e104]: visibility_off
      - generic [ref=e108]: Min 12 chars · uppercase · lowercase · number · special (!@#$%)
    - button "Create Account" [ref=e110] [cursor=pointer]:
      - generic [ref=e111]: Create Account
    - paragraph [ref=e113]: By signing up, you agree to our Terms of Service and Privacy Policy
  - paragraph [ref=e115]:
    - text: Already have an account?
    - link "Sign in" [ref=e116] [cursor=pointer]:
      - /url: /login
```

# Test source

```ts
  49  |     await page.fill('input[formControlName="email"]', 'not-an-email');
  50  |     await page.locator('input[formControlName="password"]').click();
  51  |     await expect(page.locator('mat-error')).toBeVisible();
  52  |   });
  53  | 
  54  |   test('wrong credentials shows error message', async ({ page }) => {
  55  |     await page.goto('/login');
  56  |     await page.fill('input[formControlName="email"]', 'nobody@kincare-test.invalid');
  57  |     await page.fill('input[formControlName="password"]', 'WrongPass123!');
  58  |     await page.click('button[type="submit"]');
  59  |     await expect(page.locator('text=Invalid email or password')).toBeVisible({ timeout: 10000 });
  60  |   });
  61  | 
  62  |   test('unauthenticated user redirected from /dashboard to /login', async ({ page }) => {
  63  |     await page.goto('/dashboard');
  64  |     await expect(page).toHaveURL(/\/login/);
  65  |   });
  66  | 
  67  |   test('unauthenticated user redirected from /residents to /login', async ({ page }) => {
  68  |     await page.goto('/residents');
  69  |     await expect(page).toHaveURL(/\/login/);
  70  |   });
  71  | 
  72  |   test('unauthenticated user redirected from /billing to /login', async ({ page }) => {
  73  |     await page.goto('/billing');
  74  |     await expect(page).toHaveURL(/\/login/);
  75  |   });
  76  | });
  77  | 
  78  | // ─── Login flow ───────────────────────────────────────────────────────────────
  79  | 
  80  | test.describe('Auth — login flow', () => {
  81  |   test('valid credentials redirect to dashboard', async ({ page }) => {
  82  |     const creds = getTestCredentials();
  83  |     if (!creds) return test.skip();
  84  |     await loginViaUI(page, creds);
  85  |     await expect(page).toHaveURL(/\/dashboard/);
  86  |   });
  87  | 
  88  |   test('dashboard shows after login', async ({ page }) => {
  89  |     const creds = getTestCredentials();
  90  |     if (!creds) return test.skip();
  91  |     await loginViaUI(page, creds);
  92  |     await expect(page.locator("text=Today's Rides")).toBeVisible({ timeout: 10000 });
  93  |   });
  94  | 
  95  |   test('JWT stored in localStorage after login', async ({ page }) => {
  96  |     const creds = getTestCredentials();
  97  |     if (!creds) return test.skip();
  98  |     await loginViaUI(page, creds);
  99  |     const token = await page.evaluate(() => localStorage.getItem('access_token'));
  100 |     expect(token).toBeTruthy();
  101 |     const parts = token!.split('.');
  102 |     expect(parts.length).toBe(3);
  103 |   });
  104 | 
  105 |   test('logout clears token and redirects to /login', async ({ page }) => {
  106 |     const creds = getTestCredentials();
  107 |     if (!creds) return test.skip();
  108 |     await loginViaUI(page, creds);
  109 | 
  110 |     // Find logout in nav/menu
  111 |     const logoutBtn = page.locator('button:has-text("Logout"), [data-testid="logout"]').first();
  112 |     if (await logoutBtn.isVisible()) {
  113 |       await logoutBtn.click();
  114 |       await expect(page).toHaveURL(/\/login/);
  115 |       const token = await page.evaluate(() => localStorage.getItem('access_token'));
  116 |       expect(token).toBeNull();
  117 |     } else {
  118 |       // Logout via manual token clear + navigate
  119 |       await page.evaluate(() => {
  120 |         localStorage.removeItem('access_token');
  121 |         localStorage.removeItem('refresh_token');
  122 |       });
  123 |       await page.goto('/dashboard');
  124 |       await expect(page).toHaveURL(/\/login/);
  125 |     }
  126 |   });
  127 | });
  128 | 
  129 | // ─── Registration flow ────────────────────────────────────────────────────────
  130 | 
  131 | test.describe('Auth — registration', () => {
  132 |   test('register new org via UI redirects to dashboard', async ({ page }) => {
  133 |     const ts = Date.now();
  134 |     await page.goto('/register');
  135 | 
  136 |     // Select OrgAdmin role
  137 |     await page.locator('mat-select[formControlName="role"]').click();
  138 |     await page.locator('mat-option:has-text("Org Admin")').click();
  139 | 
  140 |     await page.fill('input[formControlName="organizationName"]', `E2E Org ${ts}`);
  141 |     await page.fill('input[formControlName="facilityName"]', `E2E Facility ${ts}`);
  142 |     await page.fill('input[formControlName="facilityAddress"]', '1 Test Ave, Austin TX 78701');
  143 |     await page.fill('input[formControlName="firstName"]', 'E2E');
  144 |     await page.fill('input[formControlName="lastName"]', 'User');
  145 |     await page.fill('input[formControlName="email"]', `e2e-${ts}@kincare-test.invalid`);
  146 |     await page.fill('input[formControlName="password"]', 'E2ePassw0rd!');
  147 | 
  148 |     await page.click('button[type="submit"]');
> 149 |     await page.waitForURL('**/dashboard', { timeout: 15000 });
      |                ^ TimeoutError: page.waitForURL: Timeout 15000ms exceeded.
  150 |     await expect(page).toHaveURL(/\/dashboard/);
  151 |   });
  152 | 
  153 |   test('register with duplicate email shows error', async ({ request, page }) => {
  154 |     // First create an account via API
  155 |     const { email } = await registerNewOrg(request);
  156 | 
  157 |     // Now try to register with the same email via UI
  158 |     const ts = Date.now();
  159 |     await page.goto('/register');
  160 |     await page.locator('mat-select[formControlName="role"]').click();
  161 |     await page.locator('mat-option:has-text("Org Admin")').click();
  162 |     await page.fill('input[formControlName="organizationName"]', `Dup Org ${ts}`);
  163 |     await page.fill('input[formControlName="facilityName"]', `Dup Facility ${ts}`);
  164 |     await page.fill('input[formControlName="facilityAddress"]', '1 Dup St');
  165 |     await page.fill('input[formControlName="firstName"]', 'Dup');
  166 |     await page.fill('input[formControlName="lastName"]', 'User');
  167 |     await page.fill('input[formControlName="email"]', email);
  168 |     await page.fill('input[formControlName="password"]', 'E2ePassw0rd!');
  169 |     await page.click('button[type="submit"]');
  170 | 
  171 |     await expect(page.locator('mat-error, .alert-error, [class*="error"]').first()).toBeVisible({ timeout: 10000 });
  172 |   });
  173 | });
  174 | 
  175 | // ─── Password reset flow ──────────────────────────────────────────────────────
  176 | 
  177 | test.describe('Auth — password reset', () => {
  178 |   test('forgot-password always shows success (anti-enumeration)', async ({ page }) => {
  179 |     await page.goto('/forgot-password');
  180 |     await page.fill('input[formControlName="email"]', 'nobody@kincare-test.invalid');
  181 |     await page.click('button[type="submit"]');
  182 |     await expect(page.locator('text=Check your email')).toBeVisible({ timeout: 10000 });
  183 |   });
  184 | 
  185 |   test('API request-password-reset returns 200 for unknown email', async ({ request }) => {
  186 |     const res = await request.post(`${API_URL}/api/auth/request-password-reset`, {
  187 |       data: { email: 'nobody@kincare-test.invalid' },
  188 |     });
  189 |     expect(res.status()).toBe(200);
  190 |     const body = await res.json();
  191 |     expect(body.message).toBeTruthy();
  192 |   });
  193 | 
  194 |   test('API reset-password with invalid token returns 400', async ({ request }) => {
  195 |     const res = await request.post(`${API_URL}/api/auth/reset-password`, {
  196 |       data: { token: 'invalid-token-xyz', newPassword: 'NewPassw0rd!' },
  197 |     });
  198 |     expect(res.status()).toBe(400);
  199 |   });
  200 | 
  201 |   test('reset-password page with valid ?token param shows form', async ({ page }) => {
  202 |     await page.goto('/reset-password?token=some-token-here');
  203 |     await expect(page.locator('text=Invalid Reset Link')).not.toBeVisible();
  204 |     await expect(page.locator('input[formControlName="newPassword"]')).toBeVisible();
  205 |   });
  206 | });
  207 | 
  208 | // ─── API auth contract tests ──────────────────────────────────────────────────
  209 | 
  210 | test.describe('Auth — API contract', () => {
  211 |   test('POST /api/auth/login returns accessToken, refreshToken, role', async ({ request }) => {
  212 |     const creds = getTestCredentials();
  213 |     if (!creds) return test.skip();
  214 |     const res = await request.post(`${API_URL}/api/auth/login`, { data: creds });
  215 |     expect(res.ok()).toBeTruthy();
  216 |     const body = await res.json();
  217 |     expect(body.accessToken).toBeTruthy();
  218 |     expect(body.refreshToken).toBeTruthy();
  219 |     expect(body.role).toBeTruthy();
  220 |   });
  221 | 
  222 |   test('POST /api/auth/refresh rotates token', async ({ request }) => {
  223 |     const creds = getTestCredentials();
  224 |     if (!creds) return test.skip();
  225 |     const loginRes = await request.post(`${API_URL}/api/auth/login`, { data: creds });
  226 |     const { refreshToken } = await loginRes.json();
  227 | 
  228 |     const refreshRes = await request.post(`${API_URL}/api/auth/refresh`, {
  229 |       data: { refreshToken },
  230 |     });
  231 |     expect(refreshRes.ok()).toBeTruthy();
  232 |     const body = await refreshRes.json();
  233 |     expect(body.accessToken).toBeTruthy();
  234 |     expect(body.refreshToken).not.toBe(refreshToken); // token rotated
  235 |   });
  236 | 
  237 |   test('protected endpoint returns 401 without token', async ({ request }) => {
  238 |     const res = await request.get(`${API_URL}/api/rides/today`);
  239 |     expect(res.status()).toBe(401);
  240 |   });
  241 | 
  242 |   test('protected endpoint returns 401 with malformed token', async ({ request }) => {
  243 |     const res = await request.get(`${API_URL}/api/rides/today`, {
  244 |       headers: { Authorization: 'Bearer this.is.fake' },
  245 |     });
  246 |     expect(res.status()).toBe(401);
  247 |   });
  248 | });
  249 | 
```