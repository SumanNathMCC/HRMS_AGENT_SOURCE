using System.Data;
using Azure;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Request;
using HRMS_CHATBOT_SOURCE.Domain.Models;
using HRMS_CHATBOT_SOURCE.Foundation.Common;
using HRMS_CHATBOT_SOURCE.Infrastructure.Core;
using MCC.Foundation.MSSQLHelper.Helper;
using MCC.Foundation.MSSQLHelper.Models;
using Microsoft.Data.SqlClient;
using SqlCommon = HRMS_CHATBOT_SOURCE.Domain.Constants.Common;

namespace HRMS_CHATBOT_SOURCE.Repo.Admin;

public class UserProfileRepo : IUserProfileRepo
{
    private readonly ISqlHelper _sqlHelper;
    private readonly IServiceContext _serviceContext;

    public UserProfileRepo(ISqlHelper sqlHelper, IServiceContext serviceContext)
    {
        _sqlHelper = sqlHelper;
        _serviceContext = serviceContext;
    }

    public async Task<MSSQLResponse?> ValidateAdminLoginAsync(LoginRequest? request, CancellationToken cancellationToken = default)
    {
        var sqlParams = new SqlParameter[]
        {
            new()
            {
                ParameterName = "@userLoginName",
                DbType = DbType.String,
                Direction = ParameterDirection.Input,
                Size = -1,
                Value = Utils.IIFStringOrDBNull(request?.UserId)
            },
            new()
            {
                ParameterName = "@password",
                DbType = DbType.String,
                Direction = ParameterDirection.Input,
                Size = -1,
                Value = Utils.IIFStringOrDBNull(request?.Password)
            },
            new()
            {
                ParameterName = "@mobile",
                DbType = DbType.String,
                Direction = ParameterDirection.Input,
                Size = -1,
                Value = DBNull.Value
            },
            new()
            {
                ParameterName = "@deviceId",
                DbType = DbType.String,
                Direction = ParameterDirection.Input,
                Size = -1,
                Value = DBNull.Value
            },
            new()
            {
                ParameterName = "@outputCode",
                DbType = DbType.Int32,
                Direction = ParameterDirection.Output
            },
            new()
            {
                ParameterName = "@outputMsg",
                DbType = DbType.String,
                Direction = ParameterDirection.Output,
                Size = -1
            }
        };

        var response = new MSSQLResponse
        {
            Data = await _sqlHelper.FetchData(new ExecuteDataSetRequest
            {
                CommandText = "[dbo].[Validate_Admin_Login]",
                CommandTimeout = SqlCommon.SQLCommandTimeOut,
                CommandType = CommandType.StoredProcedure,
                ConnectionProperties = _serviceContext.SQLConnectionModel,
                IsMultipleTables = true,
                Parameters = sqlParams
            }),
            RowsAffected = null,
            OutputParameters = sqlParams.Where(p => p.Direction == ParameterDirection.Output).ToArray()
        };

        return response;
    }

    public async Task<string?> GetUserMobileByUserIdAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var sqlParams = new SqlParameter[]
        {
            new()
            {
                ParameterName = "@user_id",
                DbType = DbType.String,
                Direction = ParameterDirection.Input,
                Size = 20,
                Value = userId.Trim()
            },
            new()
            {
                ParameterName = "@mobile",
                DbType = DbType.String,
                Direction = ParameterDirection.Output,
                Size = 20
            }
        };

        var response = new MSSQLResponse
        {
            RowsAffected = await _sqlHelper.ExecuteNonQuery(new ExecuteNonQueryRequest
            {
                CommandText = "[dbo].[Get_Admin_User_Mobile]",
                CommandTimeout = SqlCommon.SQLCommandTimeOut,
                CommandType = CommandType.StoredProcedure,
                ConnectionProperties = _serviceContext.SQLConnectionModel,
                Parameters = sqlParams
            }),
            Data = null,
            OutputParameters = sqlParams.Where(p => p.Direction == ParameterDirection.Output).ToArray()
        };

        var mobile = Convert.ToString(response.OutputParameters?.FirstOrDefault(p =>
            string.Equals(p.ParameterName, "@mobile", StringComparison.OrdinalIgnoreCase))?.Value);

        return string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim();
    }

    public async Task<string?> GetUserEmailByMobileAsync(
        string? mobile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return null;
        }

        var sqlParams = new SqlParameter[]
        {
            new()
            {
                ParameterName = "@mobile",
                DbType = DbType.String,
                Direction = ParameterDirection.Input,
                Size = 20,
                Value = mobile.Trim()
            }
        };

        var response = new MSSQLResponse
        {
            Data = await _sqlHelper.FetchData(new ExecuteDataSetRequest
            {
                CommandText = "[dbo].[Get_User_Email_By_Mobile]",
                CommandTimeout = SqlCommon.SQLCommandTimeOut,
                CommandType = CommandType.StoredProcedure,
                ConnectionProperties = _serviceContext.SQLConnectionModel,
                IsMultipleTables = true,
                Parameters = sqlParams
            }),
            RowsAffected = null,
            OutputParameters = null
        };

        if (response.Data is not DataSet { Tables.Count: > 0 } dataSet
            || dataSet.Tables[0].Rows.Count == 0)
        {
            return null;
        }

        var email = Convert.ToString(dataSet.Tables[0].Rows[0]["usp_mailid"]);
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    public async Task<MSSQLResponse?> UpdateLastAccessedAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var sqlParams = new SqlParameter[]
        {
            new()
            {
                ParameterName = "@user_id",
                DbType = DbType.String,
                Direction = ParameterDirection.Input,
                Size = 20,
                Value = Utils.IIFStringOrDBNull(userId)
            },
            new()
            {
                ParameterName = "@outputCode",
                DbType = DbType.Int32,
                Direction = ParameterDirection.Output
            },
            new()
            {
                ParameterName = "@outputMsg",
                DbType = DbType.String,
                Direction = ParameterDirection.Output,
                Size = -1
            }
        };

        return new MSSQLResponse
        {
            RowsAffected = await _sqlHelper.ExecuteNonQuery(new ExecuteNonQueryRequest
            {
                CommandText = "[dbo].[Update_Admin_User_Last_Accessed]",
                CommandTimeout = SqlCommon.SQLCommandTimeOut,
                CommandType = CommandType.StoredProcedure,
                ConnectionProperties = _serviceContext.SQLConnectionModel,
                Parameters = sqlParams
            }),
            Data = null,
            OutputParameters = sqlParams.Where(p => p.Direction == ParameterDirection.Output).ToArray()
        };
    }

    public async Task<MSSQLResponse?> GetActiveMobileNumbersAsync(CancellationToken cancellationToken = default)
    {
        var sqlParams = new SqlParameter[]
        {
            new()
            {
                ParameterName = "@outputCode",
                DbType = DbType.Int32,
                Direction = ParameterDirection.Output
            },
            new()
            {
                ParameterName = "@outputMsg",
                DbType = DbType.String,
                Direction = ParameterDirection.Output,
                Size = -1
            }
        };

        return new MSSQLResponse
        {
            Data = await _sqlHelper.FetchData(new ExecuteDataSetRequest
            {
                CommandText = "[dbo].[usp_GetActiveMobileNumbers]",
                CommandTimeout = SqlCommon.SQLCommandTimeOut,
                CommandType = CommandType.StoredProcedure,
                ConnectionProperties = _serviceContext.SQLConnectionModel,
                IsMultipleTables = true,
                Parameters = sqlParams
            }),
            OutputParameters = sqlParams.Where(p => p.Direction == ParameterDirection.Output).ToArray()
        };
    }
}
