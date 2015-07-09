﻿module JwtToken

open Encodings
open System
open System.Security.Cryptography
open System.Security.Claims
open System.IdentityModel.Tokens

type Audience = {
    ClientId : string
    Secret : Base64String
    Name : string
}

type TokenCreateRequest = {         
    Issuer : string        
    UserName : string
    Password : string        
    TokenTimeSpan : TimeSpan
}

type IdentityStore = {
    getClaims : string -> Async<Claim seq>
    isValidCredentials : string -> string -> Async<bool>
    getSecurityKey : Base64String -> SecurityKey
    getSigningCredentials : SecurityKey -> SigningCredentials
}

type Token = {
    AccessToken : string        
    ExpiresIn : float        
} 

let createAudience audienceName =          
    let clientId = Guid.NewGuid().ToString("N")
    let data = Array.zeroCreate 32
    RNGCryptoServiceProvider.Create().GetBytes(data)
    let secret = data |> Base64String.create 
    {ClientId = clientId; Secret = secret; Name =  audienceName} 
    
    
let createToken tokenCreateRequest identityStore audience = 
    async {
        let! isValidCredentials = 
            identityStore.isValidCredentials tokenCreateRequest.UserName tokenCreateRequest.Password
        if isValidCredentials then                            
            let signingCredentials = (identityStore.getSecurityKey >> identityStore.getSigningCredentials) audience.Secret
            let issuedOn = Nullable DateTime.UtcNow
            let expiresBy = Nullable (DateTime.UtcNow.Add(tokenCreateRequest.TokenTimeSpan))       
            let! claims =  identityStore.getClaims tokenCreateRequest.UserName 
            let jwtSecurityToken = 
                new JwtSecurityToken(tokenCreateRequest.Issuer, audience.ClientId, claims, issuedOn, expiresBy, signingCredentials)
            let handler = new JwtSecurityTokenHandler()  
            let accessToken = handler.WriteToken(jwtSecurityToken)                
            return Some {AccessToken = accessToken; ExpiresIn = tokenCreateRequest.TokenTimeSpan.TotalSeconds}
        else return None 
    }


type TokenValidationRequest = {
    Issuer : string
    Secret : Base64String
    ClientId : string
    AccessToken : string
}

let validate tokenValidationRequest getSecurityKey = 
    let tokenValidationParameters =
        let validationParams = new TokenValidationParameters()
        validationParams.ValidAudience <- tokenValidationRequest.ClientId
        validationParams.ValidIssuer <- tokenValidationRequest.Issuer
        validationParams.ValidateLifetime <- true
        validationParams.ValidateIssuerSigningKey <- true
        validationParams.IssuerSigningKey <-  getSecurityKey tokenValidationRequest.Secret
        validationParams    
    
    try 
        let handler = new JwtSecurityTokenHandler() 
        let principal = handler.ValidateToken(tokenValidationRequest.AccessToken, tokenValidationParameters, ref null)
        principal.Claims |> Choice1Of2
    with
        | ex -> ex.Message |> Choice2Of2