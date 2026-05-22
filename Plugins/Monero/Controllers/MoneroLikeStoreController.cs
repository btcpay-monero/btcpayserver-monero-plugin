using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.Payments;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

using Monero.Wallet.Rpc;

namespace BTCPayServer.Plugins.Monero.Controllers
{
    [Route("stores/{storeId}/monerolike")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIMoneroLikeStoreController : Controller
    {
        private readonly MoneroLikeConfiguration _MoneroLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly MoneroRpcProvider _MoneroRpcProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly ILogger<UIMoneroLikeStoreController> _logger;
        private IStringLocalizer StringLocalizer { get; }

        public UIMoneroLikeStoreController(MoneroLikeConfiguration moneroLikeConfiguration,
            StoreRepository storeRepository, MoneroRpcProvider moneroRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            IStringLocalizer stringLocalizer,
            ILogger<UIMoneroLikeStoreController> logger)
        {
            _MoneroLikeConfiguration = moneroLikeConfiguration;
            _StoreRepository = storeRepository;
            _MoneroRpcProvider = moneroRpcProvider;
            _handlers = handlers;
            StringLocalizer = stringLocalizer;
            _logger = logger;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [NonAction]
        public async Task<MoneroLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return new MoneroLikePaymentMethodListViewModel()
            {
                Items = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.Select(pair =>
                    GetMoneroLikePaymentMethodViewModel(storeData, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            };
        }

        private async Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            try
            {
                if (_MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.WalletAvailable)
                {
                    return await _MoneroRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get accounts for {CryptoCode}", cryptoCode);
            }

            return null;
        }

        private MoneroLikePaymentMethodViewModel GetMoneroLikePaymentMethodViewModel(
            StoreData storeData, string cryptoCode,
            IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
        {
            var monero = storeData.GetPaymentMethodConfigs(_handlers)
                .Where(s => s.Value is MoneroPaymentPromptDetails)
                .Select(s => (PaymentMethodId: s.Key, Details: (MoneroPaymentPromptDetails)s.Value));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var settings = monero.Where(method => method.PaymentMethodId == pmi).Select(m => m.Details).SingleOrDefault();
            _MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            var accounts = accountsResponse?.Accounts.Select(account =>
                new SelectListItem(
                    $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
                    account.AccountIndex.ToString()));

            var settlementThresholdChoice = MoneroLikeSettlementThresholdChoice.StoreSpeedPolicy;
            if (settings != null && settings.InvoiceSettledConfirmationThreshold is { } confirmations)
            {
                settlementThresholdChoice = confirmations switch
                {
                    0 => MoneroLikeSettlementThresholdChoice.ZeroConfirmation,
                    1 => MoneroLikeSettlementThresholdChoice.AtLeastOne,
                    10 => MoneroLikeSettlementThresholdChoice.AtLeastTen,
                    _ => MoneroLikeSettlementThresholdChoice.Custom
                };
            }

            return new MoneroLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AccountIndex = settings?.AccountIndex ?? accountsResponse?.Accounts?.FirstOrDefault()?.AccountIndex ?? 0,
                Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text)),
                SettlementConfirmationThresholdChoice = settlementThresholdChoice,
                CustomSettlementConfirmationThreshold =
                    settings != null &&
                    settlementThresholdChoice is MoneroLikeSettlementThresholdChoice.Custom
                        ? settings.InvoiceSettledConfirmationThreshold
                        : null
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            if (!_MoneroRpcProvider.WalletFileExists(cryptoCode))
            {
                return RedirectToAction(nameof(WalletSetup), new { storeId = StoreData.Id, cryptoCode });
            }

            var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));
            return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
        }

        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(MoneroLikePaymentMethodViewModel viewModel, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode,
                    out var configurationItem))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {

                var vm = GetMoneroLikePaymentMethodViewModel(StoreData, cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                return View("/Views/Monero/GetStoreMoneroLikePaymentMethod.cshtml", vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetPaymentMethodConfig(_handlers[PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)], new MoneroPaymentPromptDetails()
            {
                AccountIndex = viewModel.AccountIndex,
                InvoiceSettledConfirmationThreshold = viewModel.SettlementConfirmationThresholdChoice switch
                {
                    MoneroLikeSettlementThresholdChoice.ZeroConfirmation => 0,
                    MoneroLikeSettlementThresholdChoice.AtLeastOne => 1,
                    MoneroLikeSettlementThresholdChoice.AtLeastTen => 10,
                    MoneroLikeSettlementThresholdChoice.Custom when viewModel.CustomSettlementConfirmationThreshold is { } custom => custom,
                    _ => null
                }
            });

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
        }

        [HttpPost("accounts/{cryptoCode}")]
        public async Task<IActionResult> AddAccount(string cryptoCode, MoneroLikePaymentMethodViewModel viewModel)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            CreateAccountResponse newAccount;
            try
            {
                newAccount = await _MoneroRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", new CreateAccountRequest
                {
                    Label = viewModel.NewAccountLabel
                });
            }
            catch (Exception ex)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = StringLocalizer["Could not create a new account: {0}", ex.Message].Value
                });
                return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
            }

            var storeData = StoreData;
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var existing = storeData.GetPaymentMethodConfig<MoneroPaymentPromptDetails>(pmi, _handlers);
            storeData.SetPaymentMethodConfig(_handlers[pmi], new MoneroPaymentPromptDetails
            {
                AccountIndex = newAccount.AccountIndex,
                InvoiceSettledConfirmationThreshold = existing?.InvoiceSettledConfirmationThreshold
            });
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
        }

        [HttpGet("connect/{cryptoCode}")]
        public IActionResult ImportViewOnlyWallet(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            if (_MoneroRpcProvider.WalletFileExists(cryptoCode))
            {
                return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
            }

            return View("/Views/Monero/ImportViewOnlyWallet.cshtml", new MoneroLikePaymentMethodViewModel { CryptoCode = cryptoCode });
        }

        [HttpPost("connect/{cryptoCode}")]
        public async Task<IActionResult> ImportViewOnlyWallet(MoneroLikePaymentMethodViewModel viewModel, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();

            if (string.IsNullOrEmpty(viewModel.PrimaryAddress))
            {
                ModelState.AddModelError(nameof(viewModel.PrimaryAddress), StringLocalizer["The primary address is required to create a new wallet."]);
            }

            if (string.IsNullOrEmpty(viewModel.PrivateViewKey))
            {
                ModelState.AddModelError(nameof(viewModel.PrivateViewKey), StringLocalizer["The private view key is required to create a new wallet."]);
            }

            if (!ModelState.IsValid)
            {
                viewModel.CryptoCode = cryptoCode;
                return View("/Views/Monero/ImportViewOnlyWallet.cshtml", viewModel);
            }

            if (_MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary))
            {
                if (summary.WalletAvailable)
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = StringLocalizer["There is already an active wallet configured for {0}. Replacing it would break any existing invoices!", cryptoCode].Value
                    });
                    return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod),
                        new { storeId = StoreData.Id, cryptoCode });
                }
            }
            try
            {
                await _MoneroRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GenerateFromKeysRequest, GenerateFromKeysResponse>("generate_from_keys", new GenerateFromKeysRequest
                {
                    PrimaryAddress = viewModel.PrimaryAddress,
                    PrivateViewKey = viewModel.PrivateViewKey,
                    WalletFileName = "wallet",
                    RestoreHeight = viewModel.RestoreHeight,
                    Password = ""
                });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["Could not generate view wallet from keys: {0}", ex.Message]);
                viewModel.CryptoCode = cryptoCode;
                return View("/Views/Monero/ImportViewOnlyWallet.cshtml", viewModel);
            }

            await _MoneroRpcProvider.UpdateSummary(cryptoCode);

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = StringLocalizer["View-only wallet created and now active"].Value
            });
            return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
        }

        [HttpGet("setup/{cryptoCode}")]
        public IActionResult WalletSetup(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            if (_MoneroRpcProvider.WalletFileExists(cryptoCode))
            {
                return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new { storeId = StoreData.Id, cryptoCode });
            }

            return View("/Views/Monero/WalletSetup.cshtml", new MoneroLikePaymentMethodViewModel { CryptoCode = cryptoCode });
        }

        public class MoneroLikePaymentMethodListViewModel
        {
            public IEnumerable<MoneroLikePaymentMethodViewModel> Items { get; set; }
        }

        public class MoneroLikePaymentMethodViewModel : IValidatableObject
        {
            public MoneroRpcProvider.MoneroLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public long AccountIndex { get; set; }
            public bool Enabled { get; set; }

            public IEnumerable<SelectListItem> Accounts { get; set; }
            [Display(Name = "Primary Public Address")]
            public string PrimaryAddress { get; set; }
            [Display(Name = "Private View Key")]
            public string PrivateViewKey { get; set; }
            [Display(Name = "Restore Height")]
            public uint RestoreHeight { get; set; }
            [Display(Name = "Consider the invoice settled when the payment transaction …")]
            public MoneroLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public int? CustomSettlementConfirmationThreshold { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (SettlementConfirmationThresholdChoice is MoneroLikeSettlementThresholdChoice.Custom
                    && CustomSettlementConfirmationThreshold is null)
                {
                    yield return new ValidationResult(
                        "You must specify the number of required confirmations when using a custom threshold.",
                        new[] { nameof(CustomSettlementConfirmationThreshold) });
                }
            }
        }

        public enum MoneroLikeSettlementThresholdChoice
        {
            [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
            StoreSpeedPolicy,
            [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
            ZeroConfirmation,
            [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
            AtLeastOne,
            [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
            AtLeastTen,
            [Display(Name = "Custom", Description = "Custom")]
            Custom
        }
    }
}