// Admin.js - Fixed version without variable conflicts
(function () {
    'use strict';

    const ADMIN_API_BASE = '/api';
    let allBookings = [];
    let isAdminLoggedIn = false;

    // Блокуємо помилки Stripe в консолі
    window.addEventListener('error', function (e) {
        if (e.target && (e.target.src && e.target.src.includes('stripe.com'))) {
            e.preventDefault();
            return false;
        }
    }, true);

    // Блокуємо помилки fetch для Stripe
    const originalFetch = window.fetch;
    window.fetch = function (...args) {
        const url = args[0];
        if (typeof url === 'string' && url.includes('stripe.com')) {
            return Promise.reject(new Error('Stripe blocked'));
        }
        return originalFetch.apply(this, args);
    };

    // Глобальна функція для завантаження адмін панелі
    window.loadAdminDashboard = async function () {
        const adminContent = document.getElementById('adminContent');
        if (!adminContent) {
            console.error('Admin content div not found');
            return;
        }

        console.log('Loading admin dashboard...');

        // Створюємо HTML для адмін панелі
        adminContent.innerHTML = `
            <div class="row mb-4">
                <div class="col-md-3">
                    <div class="stat-card primary">
                        <div class="d-flex justify-content-between align-items-center">
                            <div>
                                <h6 class="text-muted mb-1">Total Bookings</h6>
                                <h3 class="mb-0" id="totalBookings">Loading...</h3>
                            </div>
                            <i class="bi bi-calendar-check text-primary" style="font-size: 2rem;"></i>
                        </div>
                    </div>
                </div>
                <div class="col-md-3">
                    <div class="stat-card success">
                        <div class="d-flex justify-content-between align-items-center">
                            <div>
                                <h6 class="text-muted mb-1">Assigned</h6>
                                <h3 class="mb-0" id="assignedBookings">Loading...</h3>
                            </div>
                            <i class="bi bi-check-circle text-success" style="font-size: 2rem;"></i>
                        </div>
                    </div>
                </div>
                <div class="col-md-3">
                    <div class="stat-card warning">
                        <div class="d-flex justify-content-between align-items-center">
                            <div>
                                <h6 class="text-muted mb-1">Pending</h6>
                                <h3 class="mb-0" id="pendingBookings">Loading...</h3>
                            </div>
                            <i class="bi bi-clock text-warning" style="font-size: 2rem;"></i>
                        </div>
                    </div>
                </div>
                <div class="col-md-3">
                    <div class="stat-card danger">
                        <div class="d-flex justify-content-between align-items-center">
                            <div>
                                <h6 class="text-muted mb-1">Total Revenue</h6>
                                <h3 class="mb-0" id="totalRevenue">Loading...</h3>
                            </div>
                            <i class="bi bi-currency-dollar text-info" style="font-size: 2rem;"></i>
                        </div>
                    </div>
                </div>
            </div>

            <div class="card shadow">
                <div class="card-header bg-white">
                    <div class="row align-items-center">
                        <div class="col">
                            <h5 class="mb-0"><i class="bi bi-table"></i> Booking Management</h5>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-primary btn-sm me-2" onclick="window.adminFunctions.refreshBookings()">
                                <i class="bi bi-arrow-clockwise"></i> Refresh
                            </button>
                            <button class="btn btn-success btn-sm" onclick="window.adminFunctions.exportData()">
                                <i class="bi bi-download"></i> Export CSV
                            </button>
                        </div>
                    </div>
                </div>
                <div class="card-body">
                    <div class="row mb-3">
                        <div class="col-md-4">
                            <input type="text" id="searchFilter" class="form-control" 
                                   placeholder="Search by ID, name or email..." onkeyup="window.adminFunctions.filterBookings()">
                        </div>
                        <div class="col-md-4">
                            <select id="statusFilter" class="form-select" onchange="window.adminFunctions.filterBookings()">
                                <option value="">All Status</option>
                                <option value="Created">Created</option>
                                <option value="PaymentPending">Payment Pending</option>
                                <option value="PaymentConfirmed">Payment Confirmed</option>
                                <option value="ExaminersContacted">Examiners Contacted</option>
                                <option value="ExaminerAssigned">Examiner Assigned</option>
                                <option value="Scheduled">Scheduled</option>
                                <option value="Completed">Completed</option>
                                <option value="Cancelled">Cancelled</option>
                            </select>
                        </div>
                        <div class="col-md-4">
                            <input type="date" id="dateFilter" class="form-control" onchange="window.adminFunctions.filterBookings()">
                        </div>
                    </div>
                    <div class="table-responsive">
                        <table class="table table-hover">
                            <thead class="table-light">
                                <tr>
                                    <th>Booking ID</th>
                                    <th>Student</th>
                                    <th>Email</th>
                                    <th>Exam Type</th>
                                    <th>Status</th>
                                    <th>Payment</th>
                                    <th>Examiner</th>
                                    <th>Created</th>
                                    <th>Actions</th>
                                </tr>
                            </thead>
                            <tbody id="bookingsTableBody">
                                <tr>
                                    <td colspan="9" class="text-center py-4">
                                        <div class="spinner-border text-primary" role="status">
                                            <span class="visually-hidden">Loading...</span>
                                        </div>
                                        <div class="mt-2">Loading bookings...</div>
                                    </td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
        `;

        // Завантажуємо дані
        try {
            await Promise.all([loadBookings(), loadStatistics()]);
            console.log('Admin dashboard loaded successfully');
        } catch (error) {
            console.error('Error loading admin dashboard:', error);
            adminContent.innerHTML = `
                <div class="alert alert-danger">
                    <h5>Error Loading Dashboard</h5>
                    <p>There was an error loading the admin dashboard: ${error.message}</p>
                    <button class="btn btn-danger" onclick="window.loadAdminDashboard()">Try Again</button>
                </div>
            `;
        }
    };

    // Завантаження статистики
    async function loadStatistics() {
        try {
            console.log('Loading statistics...');
            const response = await fetch(`${ADMIN_API_BASE}/Admin/dashboard-stats`);

            if (response.ok) {
                const stats = await response.json();
                console.log('Statistics loaded:', stats);

                // Безпечне оновлення елементів
                const updateElement = (id, value, defaultValue = '0') => {
                    const element = document.getElementById(id);
                    if (element) {
                        element.textContent = value || defaultValue;
                    }
                };

                updateElement('totalBookings', stats.totalBookings || stats.TotalBookings);
                updateElement('assignedBookings', stats.assignedBookings || stats.AssignedBookings);
                updateElement('pendingBookings', stats.pendingBookings || stats.PendingBookings);
                updateElement('totalRevenue', `$${stats.totalRevenue || stats.TotalRevenue || '0'}`);

            } else {
                console.error('Failed to load statistics:', response.status, response.statusText);
                // Показуємо базові значення при помилці
                ['totalBookings', 'assignedBookings', 'pendingBookings'].forEach(id => {
                    const element = document.getElementById(id);
                    if (element) element.textContent = 'Error';
                });
                const revenueElement = document.getElementById('totalRevenue');
                if (revenueElement) revenueElement.textContent = '$0';
            }
        } catch (error) {
            console.error('Error loading statistics:', error);
            // Показуємо помилку в елементах
            ['totalBookings', 'assignedBookings', 'pendingBookings'].forEach(id => {
                const element = document.getElementById(id);
                if (element) element.textContent = 'Error';
            });
        }
    }

    // Завантаження букінгів
    async function loadBookings() {
        try {
            console.log('Loading bookings...');
            const response = await fetch(`${ADMIN_API_BASE}/Admin/bookings`);

            if (response.ok) {
                allBookings = await response.json();
                console.log('Bookings loaded:', allBookings.length);
                displayBookings(allBookings);
            } else {
                console.error('Failed to load bookings:', response.status);
                const tbody = document.getElementById('bookingsTableBody');
                if (tbody) {
                    tbody.innerHTML = '<tr><td colspan="9" class="text-center text-danger">Failed to load bookings</td></tr>';
                }
            }
        } catch (error) {
            console.error('Error loading bookings:', error);
            const tbody = document.getElementById('bookingsTableBody');
            if (tbody) {
                tbody.innerHTML = '<tr><td colspan="9" class="text-center text-danger">Network error loading bookings</td></tr>';
            }
        }
    }

    // Відображення букінгів
    function displayBookings(bookings) {
        const tbody = document.getElementById('bookingsTableBody');
        if (!tbody) return;

        if (!bookings || bookings.length === 0) {
            tbody.innerHTML = '<tr><td colspan="9" class="text-center text-muted">No bookings found</td></tr>';
            return;
        }

        tbody.innerHTML = bookings.map(booking => `
            <tr>
                <td><code class="text-primary">${booking.bookingId || booking.BookingId}</code></td>
                <td>${booking.studentName || booking.StudentName}</td>
                <td><small>${booking.studentEmail || booking.StudentEmail}</small></td>
                <td><span class="badge bg-light text-dark">${booking.examType || booking.ExamType || '-'}</span></td>
                <td>${getStatusBadge(booking.status || booking.Status)}</td>
                <td>${(booking.isPaid || booking.IsPaid) ?
                '<span class="badge bg-success">Paid</span>' :
                '<span class="badge bg-warning">Pending</span>'}</td>
                <td>${booking.assignedExaminerName || booking.AssignedExaminerName || '-'}</td>
                <td><small>${new Date(booking.createdAt || booking.CreatedAt).toLocaleDateString()}</small></td>
                <td>
                    <div class="btn-group" role="group">
                        <button class="btn btn-sm btn-outline-info" onclick="window.adminFunctions.viewDetails('${booking.bookingId || booking.BookingId}')" title="View Details">
                            <i class="bi bi-eye"></i>
                        </button>
                        ${(!booking.assignedExaminerName && !booking.AssignedExaminerName && (booking.isPaid || booking.IsPaid)) ?
                `<button class="btn btn-sm btn-outline-danger" onclick="window.adminFunctions.processRefund('${booking.bookingId || booking.BookingId}')" title="Process Refund">
                                <i class="bi bi-cash"></i>
                            </button>` : ''}
                    </div>
                </td>
            </tr>
        `).join('');
    }

    // Отримання badge для статусу
    function getStatusBadge(status) {
        const badges = {
            'Created': '<span class="badge bg-secondary">Created</span>',
            'PaymentPending': '<span class="badge bg-warning">Payment Pending</span>',
            'PaymentConfirmed': '<span class="badge bg-info">Payment Confirmed</span>',
            'ExaminersContacted': '<span class="badge bg-info">Examiners Contacted</span>',
            'ExaminerAssigned': '<span class="badge bg-success">Examiner Assigned</span>',
            'Scheduled': '<span class="badge bg-primary">Scheduled</span>',
            'Completed': '<span class="badge bg-dark">Completed</span>',
            'Cancelled': '<span class="badge bg-danger">Cancelled</span>',
            'Refunded': '<span class="badge bg-secondary">Refunded</span>'
        };
        return badges[status] || `<span class="badge bg-secondary">${status}</span>`;
    }

    // Створюємо об'єкт з функціями для доступу ззовні
    window.adminFunctions = {
        // Фільтрація букінгів
        filterBookings: function () {
            const searchFilter = document.getElementById('searchFilter');
            const statusFilter = document.getElementById('statusFilter');
            const dateFilter = document.getElementById('dateFilter');

            let filtered = [...allBookings];

            if (statusFilter && statusFilter.value) {
                filtered = filtered.filter(b => (b.status || b.Status) === statusFilter.value);
            }

            if (dateFilter && dateFilter.value) {
                filtered = filtered.filter(b => {
                    const bookingDate = new Date(b.createdAt || b.CreatedAt).toDateString();
                    const filterDate = new Date(dateFilter.value).toDateString();
                    return bookingDate === filterDate;
                });
            }

            if (searchFilter && searchFilter.value) {
                const search = searchFilter.value.toLowerCase();
                filtered = filtered.filter(b => {
                    const bookingId = (b.bookingId || b.BookingId || '').toLowerCase();
                    const studentName = (b.studentName || b.StudentName || '').toLowerCase();
                    const studentEmail = (b.studentEmail || b.StudentEmail || '').toLowerCase();

                    return bookingId.includes(search) ||
                        studentName.includes(search) ||
                        studentEmail.includes(search);
                });
            }

            displayBookings(filtered);
        },

        // Оновлення даних
        refreshBookings: async function () {
            console.log('Refreshing bookings...');
            const refreshBtn = document.querySelector('button[onclick="window.adminFunctions.refreshBookings()"]');
            if (refreshBtn) {
                refreshBtn.disabled = true;
                refreshBtn.innerHTML = '<i class="bi bi-arrow-clockwise spinner-border spinner-border-sm"></i> Refreshing...';
            }

            try {
                await Promise.all([loadBookings(), loadStatistics()]);
            } catch (error) {
                console.error('Error refreshing:', error);
            } finally {
                if (refreshBtn) {
                    refreshBtn.disabled = false;
                    refreshBtn.innerHTML = '<i class="bi bi-arrow-clockwise"></i> Refresh';
                }
            }
        },

        // Експорт даних в CSV
        exportData: function () {
            if (!allBookings || allBookings.length === 0) {
                alert('No data to export');
                return;
            }

            const headers = ['Booking ID', 'Student Name', 'Email', 'Exam Type', 'Status', 'Payment', 'Examiner', 'Created'];
            const rows = allBookings.map(b => [
                b.bookingId || b.BookingId,
                b.studentName || b.StudentName,
                b.studentEmail || b.StudentEmail,
                b.examType || b.ExamType || '',
                b.status || b.Status,
                (b.isPaid || b.IsPaid) ? 'Paid' : 'Pending',
                b.assignedExaminerName || b.AssignedExaminerName || '',
                new Date(b.createdAt || b.CreatedAt).toLocaleDateString()
            ]);

            let csv = headers.join(',') + '\n';
            rows.forEach(row => {
                csv += row.map(cell => `"${cell}"`).join(',') + '\n';
            });

            const blob = new Blob([csv], { type: 'text/csv' });
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `bookings_${new Date().toISOString().split('T')[0]}.csv`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);
        },

        // Перегляд деталей букінгу
        viewDetails: function (bookingId) {
            console.log(`Loading details for booking: ${bookingId}`);

            fetch(`/api/Admin/examiner-responses/${bookingId}`)
                .then(response => {
                    console.log('Response status:', response.status);
                    if (!response.ok) {
                        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
                    }
                    return response.json();
                })
                .then(data => {
                    console.log('Received data:', data);

                    // БЕЗПЕЧНІ значення за замовчуванням з правильним регістром
                    const examinerResponses = data.examinerResponses || data.ExaminerResponses || [];
                    const actionHistory = data.actionHistory || data.ActionHistory || [];
                    const totalContacted = data.totalContacted || data.TotalContacted || 0;
                    const totalResponded = data.totalResponded || data.TotalResponded || 0;
                    const acceptedCount = data.acceptedCount || data.AcceptedCount || 0;
                    const declinedCount = data.declinedCount || data.DeclinedCount || 0;

                    let modalHtml = `
    <div class="modal fade" id="detailsModal" tabindex="-1">
        <div class="modal-dialog modal-xl">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Booking Details: ${bookingId}</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                </div>
                <div class="modal-body">
                    <h6>Statistics</h6>
                    <div class="row mb-3">
                        <div class="col-md-3">
                            <div class="card text-center">
                                <div class="card-body">
                                    <h5>${totalContacted}</h5>
                                    <small>Contacted</small>
                                </div>
                            </div>
                        </div>
                        <div class="col-md-3">
                            <div class="card text-center">
                                <div class="card-body">
                                    <h5>${totalResponded}</h5>
                                    <small>Responded</small>
                                </div>
                            </div>
                        </div>
                        <div class="col-md-3">
                            <div class="card text-center">
                                <div class="card-body">
                                    <h5 class="text-success">${acceptedCount}</h5>
                                    <small>Accepted</small>
                                </div>
                            </div>
                        </div>
                        <div class="col-md-3">
                            <div class="card text-center">
                                <div class="card-body">
                                    <h5 class="text-danger">${declinedCount}</h5>
                                    <small>Declined</small>
                                </div>
                            </div>
                        </div>
                    </div>
                    
                    <h6>Examiner Responses</h6>`;

                    if (examinerResponses && examinerResponses.length > 0) {
                        modalHtml += `
        <table class="table table-sm">
            <thead>
                <tr>
                    <th>Examiner</th>
                    <th>Response</th>
                    <th>Contacted</th>
                    <th>Responded</th>
                    <th>Message</th>
                    <th>Winner</th>
                </tr>
            </thead>
            <tbody>`;

                        examinerResponses.forEach(r => {
                            // Безпечна обробка властивостей
                            const examinerName = r.examinerName || r.ExaminerName || 'Unknown';
                            const examinerEmail = r.examinerEmail || r.ExaminerEmail || '';
                            const response = r.response || r.Response || 'NoResponse';
                            const contactedAt = r.contactedAt || r.ContactedAt;
                            const respondedAt = r.respondedAt || r.RespondedAt;
                            const responseMessage = r.responseMessage || r.ResponseMessage || '-';
                            const isWinner = r.isWinner || r.IsWinner || false;

                            modalHtml += `
            <tr>
                <td>${examinerName}<br><small>${examinerEmail}</small></td>
                <td>${response === 'Accepted' ?
                                    '<span class="badge bg-success">Accepted</span>' :
                                    response === 'Declined' ?
                                        '<span class="badge bg-danger">Declined</span>' :
                                        '<span class="badge bg-secondary">No Response</span>'}</td>
                <td>${contactedAt ? new Date(contactedAt).toLocaleString() : '-'}</td>
                <td>${respondedAt ? new Date(respondedAt).toLocaleString() : '-'}</td>
                <td>${responseMessage}</td>
                <td>${isWinner ? '✅' : ''}</td>
            </tr>`;
                        });

                        modalHtml += `</tbody></table>`;
                    } else {
                        modalHtml += '<p class="text-muted">No examiner responses found</p>';
                    }

                    modalHtml += `<h6>Action History</h6>`;

                    if (actionHistory && actionHistory.length > 0) {
                        modalHtml += '<div class="timeline" style="max-height: 300px; overflow-y: auto;">';
                        actionHistory.forEach(action => {
                            const actionType = action.actionType || action.ActionType || 'Unknown';
                            const description = action.description || action.Description || '';
                            const details = action.details || action.Details || '';
                            const createdAt = action.createdAt || action.CreatedAt;

                            modalHtml += `
            <div class="mb-2 p-2 border-start border-3">
                <small class="text-muted">${createdAt ? new Date(createdAt).toLocaleString() : 'Unknown date'}</small>
                <div><strong>${actionType}</strong>: ${description}</div>
                ${details ? `<small class="text-muted">${details}</small>` : ''}
            </div>`;
                        });
                        modalHtml += '</div>';
                    } else {
                        modalHtml += '<p class="text-muted">No action history found</p>';
                    }

                    modalHtml += `
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>
                </div>
            </div>
        </div>
    </div>`;

                    // Видаляємо старий модал
                    const oldModal = document.getElementById('detailsModal');
                    if (oldModal) {
                        oldModal.remove();
                    }

                    // Додаємо новий модал
                    document.body.insertAdjacentHTML('beforeend', modalHtml);

                    // Показуємо модал
                    const modal = new bootstrap.Modal(document.getElementById('detailsModal'));
                    modal.show();
                })
                .catch(error => {
                    console.error('Error loading details:', error);
                    alert(`Failed to load booking details: ${error.message}`);
                });
        },

        // Обробка повернення коштів
        processRefund: async function (bookingId) {
            if (!confirm(`Are you sure you want to process a refund for booking ${bookingId}?`)) return;

            try {
                const response = await fetch(`${ADMIN_API_BASE}/Admin/process-refund/${bookingId}`, {
                    method: 'POST'
                });

                if (response.ok) {
                    alert('Refund processed successfully');
                    await window.adminFunctions.refreshBookings();
                } else {
                    const error = await response.text();
                    alert(`Failed to process refund: ${error}`);
                }
            } catch (error) {
                console.error('Error processing refund:', error);
                alert('Error processing refund: Network error');
            }
        }
    };

})();