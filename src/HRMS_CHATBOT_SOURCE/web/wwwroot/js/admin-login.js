(function () {
    'use strict';

    function initLoginPage() {
        const form = document.getElementById('loginForm');
        const loginBtn = document.getElementById('loginBtn');
        const loginError = document.getElementById('loginError');
        const togglePassword = document.getElementById('togglePassword');
        const passwordInput = document.getElementById('passwordInput');
        const eyeIcon = document.getElementById('eyeIcon');
        const returnUrlInput = document.getElementById('returnUrl');
        const userIdInput = document.getElementById('userIdInput');
        const rememberMeInput = document.getElementById('rememberMeInput');
        const loginUrl = form?.dataset.loginUrl || '/Admin/Account/ValidateLogin';

        let isSubmitting = false;

        window.HrmsAdminAuth?.clearToken();

        if (togglePassword && passwordInput && eyeIcon) {
            togglePassword.addEventListener('click', function () {
                const isPassword = passwordInput.type === 'password';
                passwordInput.type = isPassword ? 'text' : 'password';
                eyeIcon.setAttribute('icon', isPassword ? 'ph:eye-slash' : 'ph:eye');
            });
        }

        if (loginBtn) {
            loginBtn.addEventListener('click', function (event) {
                event.preventDefault();
                void submitLogin();
            });
        }

        if (form) {
            form.addEventListener('submit', function (event) {
                event.preventDefault();
                void submitLogin();
            });
        }

        [userIdInput, passwordInput].forEach(function (input) {
            if (!input) {
                return;
            }

            input.addEventListener('keydown', function (event) {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    void submitLogin();
                }
            });

            input.addEventListener('focus', function () {
                this.closest('.input-wrap')?.classList.add('focused');
            });

            input.addEventListener('blur', function () {
                this.closest('.input-wrap')?.classList.remove('focused');
            });
        });

        async function submitLogin() {
            if (isSubmitting) {
                return;
            }

            const userId = userIdInput?.value?.trim() || '';
            const password = passwordInput?.value || '';
            const rememberMe = rememberMeInput?.checked ?? false;

            if (!userId || !password) {
                showError('User ID and password are required.');
                return;
            }

            isSubmitting = true;
            setLoading(true);
            hideError();
            window.HrmsAdminAuth?.clearToken();

            try {
                const controller = new AbortController();
                const timeoutId = window.setTimeout(function () {
                    controller.abort();
                }, 60000);

                const response = await fetch(loginUrl, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: {
                        'Content-Type': 'application/json',
                        'Accept': 'application/json'
                    },
                    body: JSON.stringify({
                        user_id: userId,
                        password: password,
                        remember_me: rememberMe
                    }),
                    signal: controller.signal
                });

                window.clearTimeout(timeoutId);

                const responseText = await response.text();
                let data = {};
                if (responseText) {
                    try {
                        data = JSON.parse(responseText);
                    } catch {
                        data = { message: responseText.trim() };
                    }
                }

                if (!response.ok) {
                    showError(data.error_message || data.message || 'Invalid user ID or password.');
                    shakeForm();
                    return;
                }

                if (!data.access_token) {
                    showError('Login succeeded but no access token was returned.');
                    shakeForm();
                    return;
                }

                window.HrmsAdminAuth?.saveToken(data.access_token, rememberMe);
                window.HrmsAdminAuth?.saveUserProfile(data.user);

                const returnUrl = returnUrlInput?.value;
                window.location.href = returnUrl && returnUrl.startsWith('/') ? returnUrl : '/Admin/Dashboard';
            } catch (error) {
                if (error && error.name === 'AbortError') {
                    showError('Login request timed out. Please try again.');
                } else {
                    showError('Unable to connect. Please try again.');
                }
                shakeForm();
            } finally {
                isSubmitting = false;
                setLoading(false);
            }
        }

        function showError(message) {
            if (!loginError) {
                return;
            }

            loginError.textContent = message;
            loginError.style.display = 'block';
        }

        function hideError() {
            if (loginError) {
                loginError.style.display = 'none';
            }
        }

        function setLoading(loading) {
            if (!loginBtn) {
                return;
            }

            loginBtn.classList.toggle('loading', loading);
            loginBtn.disabled = loading;
            loginBtn.setAttribute('aria-busy', loading ? 'true' : 'false');

            const label = loginBtn.querySelector('.btn-label');
            if (label) {
                label.textContent = loading ? 'Signing in...' : 'Sign In';
            }
        }

        function shakeForm() {
            if (!form) {
                return;
            }

            form.classList.add('shake');
            window.setTimeout(function () {
                form.classList.remove('shake');
            }, 500);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initLoginPage);
    } else {
        initLoginPage();
    }
})();
