﻿using System;
using System.ComponentModel.Composition;
using System.ComponentModel.DataAnnotations;
using System.Reactive.Linq;
using System.Windows.Input;
using GitHub.Authentication;
using GitHub.Exports;
using GitHub.Services;
using GitHub.Validation;
using NullGuard;
using Octokit;
using ReactiveUI;

namespace GitHub.ViewModels
{
    [Export(typeof(ITwoFactorViewModel))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class TwoFactorDialogViewModel : ReactiveValidatableObject, ITwoFactorViewModel
    {
        bool isAuthenticationCodeSent;
        string authenticationCode;
        TwoFactorType twoFactorType;
        readonly ObservableAsPropertyHelper<string> description;
        readonly ObservableAsPropertyHelper<bool> isShowing;
        readonly ObservableAsPropertyHelper<bool> isSms;

        [ImportingConstructor]
        public TwoFactorDialogViewModel(IBrowser browser, IServiceProvider serviceProvider) : base(serviceProvider)
        {
            OkCommand = ReactiveCommand.Create(this.WhenAny(
                x => x.IsValid,
                x => x.AuthenticationCode,
                (valid, y) => valid.Value && (String.IsNullOrEmpty(y.Value) || (y.Value != null && y.Value.Length == 6))));
            CancelCommand = new ReactiveCommand<RecoveryOptionResult>(Observable.Return(true), _ => null);
            ShowHelpCommand = new ReactiveCommand<RecoveryOptionResult>(Observable.Return(true), _ => null);
            //ShowHelpCommand.Subscribe(x => browser.OpenUrl(twoFactorHelpUri));
            ResendCodeCommand = new ReactiveCommand<RecoveryOptionResult>(Observable.Return(true), _ => null);

            description = this.WhenAny(x => x.TwoFactorType, x => x.Value)
                .Select(type =>
                {
                    switch (type)
                    {
                        case TwoFactorType.Sms:
                            return "We sent you a message via SMS with your authentication code.";
                        case TwoFactorType.AuthenticatorApp:
                            return "Open the two-factor authentication app on your device to view your " +
                                "authentication code.";
                        case TwoFactorType.Unknown:
                            return "Enter a login authentication code here";

                        default:
                            return null;
                    }
                })
                .ToProperty(this, x => x.Description);

            isShowing = this.WhenAny(x => x.TwoFactorType, x => x.Value)
                .Select(factorType => factorType != TwoFactorType.None)
                .ToProperty(this, x => x.IsShowing);

            isSms = this.WhenAny(x => x.TwoFactorType, x => x.Value)
                .Select(factorType => factorType == TwoFactorType.Sms)
                .ToProperty(this, x => x.IsSms);
        }

        public TwoFactorType TwoFactorType
        {
            get { return twoFactorType; }
            private set { this.RaiseAndSetIfChanged(ref twoFactorType, value); }
        }

        public bool IsShowing
        {
            get { return isShowing.Value; }
        }

        public bool IsSms
        {
            get { return isSms.Value; }
        }

        public bool IsAuthenticationCodeSent
        {
            get { return isAuthenticationCodeSent; }
            private set { this.RaiseAndSetIfChanged(ref isAuthenticationCodeSent, value); }
        }

        public string Description
        {
            [return: AllowNull]
            get { return description.Value; }
        }

        [Required(ErrorMessage = "Please enter your authentication code")]
        [RegularExpression(@"\d+", ErrorMessage = "Authentication code must only contain numbers")]
        [AllowNull]
        public string AuthenticationCode
        {
            [return: AllowNull]
            get { return authenticationCode; }
            set { this.RaiseAndSetIfChanged(ref authenticationCode, value); }
        }

        public ReactiveCommand<object> OkCommand { get; private set; }
        public ReactiveCommand<RecoveryOptionResult> CancelCommand { get; private set; }
        public ReactiveCommand<RecoveryOptionResult> ShowHelpCommand { get; private set; }
        public ReactiveCommand<RecoveryOptionResult> ResendCodeCommand { get; private set; }

        public ICommand OkCmd { get { return OkCommand; } }
        public ICommand CancelCmd { get { return CancelCommand; } }
        public ICommand ShowHelpCmd { get { return ShowHelpCommand; } }
        public ICommand ResendCodeCmd { get { return ResendCodeCommand; } }

        public IObservable<RecoveryOptionResult> Show(TwoFactorRequiredUserError error)
        {
            TwoFactorType = error.TwoFactorType;
            var ok = OkCommand
                .Where(x => Validate())
                .Select(_ => AuthenticationCode == null
                    ? RecoveryOptionResult.CancelOperation
                    : RecoveryOptionResult.RetryOperation)
                .Do(_ => error.ChallengeResult = AuthenticationCode != null
                    ? new TwoFactorChallengeResult(AuthenticationCode)
                    : null);
            var cancel = CancelCommand.Select(_ => RecoveryOptionResult.CancelOperation);
            var resend = ResendCodeCommand.Select(_ => RecoveryOptionResult.RetryOperation)
                .Do(_ => error.ChallengeResult = TwoFactorChallengeResult.RequestResendCode);

            return Observable.Merge(ok, cancel, resend)
                .Take(1)
                .Do(_ =>
                {
                    bool authenticationCodeSent = error.ChallengeResult == TwoFactorChallengeResult.RequestResendCode;
                    if (!authenticationCodeSent)
                    {
                        TwoFactorType = TwoFactorType.None;
                    }
                    IsAuthenticationCodeSent = authenticationCodeSent;
                })
                .Finally(() =>
                {
                    AuthenticationCode = null;
                    //TODO: ResetValidation();
                });
        }
    }
}
