using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibraryApp.Application.Borrowing.Dto;

public sealed record StaffBorrowingSummary(
    Guid StaffMemberId,
    int ActiveLoansCount,
    int OverdueLoansCount,
    int TotalBorrowedCount);