import os
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt

# blocked = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260612_113556/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
# dark = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260612_114250/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
# bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260612_114810/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
# original = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260612_115404/fft_analysis/interleaved_fft_low_frequency_loglog.csv')


# df_blocked = pd.read_csv(blocked)
# df_dark = pd.read_csv(dark)
# df_bandpass = pd.read_csv(bandpass)
# df_original = pd.read_csv(original)

def plot_fft_comparison():
    BPD_off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_151246/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    Highpass_bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_144521/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    Highpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_150306/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    Highpass_bandpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_163344/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    df_BPD_off = pd.read_csv(BPD_off)
    df_Highpass_bandpass = pd.read_csv(Highpass_bandpass)
    df_Highpass_attenuator = pd.read_csv(Highpass_attenuator)
    df_Highpass_bandpass_attenuator = pd.read_csv(Highpass_bandpass_attenuator)

    fig, axes = plt.subplots(2, 1, figsize=(16, 9), sharey=True)   
    ax1, ax4 = axes[0], axes[1]

    # ax1.loglog(df_original['Frequency_Hz'], df_original['Data_1_1 interleaved ch1'], label='OPO + SHG + Highpass + 20 dB Attenuator @800 nm, 2.09 urad^2/rtHz', linewidth=1)
    # ax1.loglog(df_bandpass['Frequency_Hz'], df_bandpass['Data_1_1 interleaved ch1'], label='OPO + SHG + Highpass + Bandpass @800 nm, 2.09 urad^2/rtHz', linewidth=1)
    # ax1.loglog(df_dark['Frequency_Hz'], df_dark['Data_1_1 interleaved ch1'], label='BPDs On + No Incident Light', linewidth=1)
    # ax1.loglog(df_blocked['Frequency_Hz'], df_blocked['Data_1_1 interleaved ch1'], label='BPDs Off', linewidth=1)
    
    ax1.loglog(df_BPD_off['Frequency_Hz'], df_BPD_off['Data_1_1 interleaved ch1'], label='BPDs Off', linewidth=1)
    ax1.loglog(df_Highpass_bandpass['Frequency_Hz'], df_Highpass_bandpass['Data_1_1 interleaved ch1'], label='Highpass + Bandpass', linewidth=1)
    ax1.loglog(df_Highpass_attenuator['Frequency_Hz'], df_Highpass_attenuator['Data_1_1 interleaved ch1'], label='Highpass + Attenuator', linewidth=1)
    ax1.loglog(df_Highpass_bandpass_attenuator['Frequency_Hz'], df_Highpass_bandpass_attenuator['Data_1_1 interleaved ch1'], label='Highpass + Bandpass + Attenuator', linewidth=1)
    ax1.set_xlabel('Frequency_Hz')
    ax1.set_ylabel('PSD (urad^2/rtHz)')
    ax1.set_title('Data_1_1 interleaved ch1')
    ax1.legend()
    ax1.grid(True, 'both', axis='both')

    # ax4.loglog(df_original['Frequency_Hz'], df_original['Data_2_1 interleaved ch2'], label='OPO + SHG + Highpass + 20 dB Attenuator @800 nm, 2.09 urad^2/rtHz', linewidth=1)
    # ax4.loglog(df_bandpass['Frequency_Hz'], df_bandpass['Data_2_1 interleaved ch2'], label='OPO + SHG + Highpass + Bandpass @800 nm, 2.02 urad^2/rtHz', linewidth=1)
    # ax4.loglog(df_dark['Frequency_Hz'], df_dark['Data_2_1 interleaved ch2'], label='BPDs On + No Incident Light', linewidth=1)
    # ax4.loglog(df_blocked['Frequency_Hz'], df_blocked['Data_2_1 interleaved ch2'], label='BPDs Off', linewidth=1)
    
    ax4.loglog(df_BPD_off['Frequency_Hz'], df_BPD_off['Data_2_1 interleaved ch2'], label='BPDs Off', linewidth=1)
    ax4.loglog(df_Highpass_bandpass['Frequency_Hz'], df_Highpass_bandpass['Data_2_1 interleaved ch2'], label='Highpass + Bandpass', linewidth=1)
    ax4.loglog(df_Highpass_attenuator['Frequency_Hz'], df_Highpass_attenuator['Data_2_1 interleaved ch2'], label='Highpass + Attenuator', linewidth=1)
    ax4.loglog(df_Highpass_bandpass_attenuator['Frequency_Hz'], df_Highpass_bandpass_attenuator['Data_2_1 interleaved ch2'], label='Highpass + Bandpass + Attenuator', linewidth=1)
    ax4.set_xlabel('Frequency_Hz')
    ax4.set_ylabel('PSD (urad^2/rtHz)')
    ax4.set_title('Data_2_1 interleaved ch2')
    ax4.legend()
    ax4.grid(True, 'both', axis='both')


    fig.suptitle('Electric Noise FFT Log-Log Comparison by Channel')
    footnote = 'No light input for all experiments, detectors blocked.'
    fig.text(0.01, 0.01, footnote, ha='left', va='bottom', fontsize=10, color='gray')
    plt.tight_layout()
    output_dir = 'D:/Quantum Squeezing Project/DataFiles/FFT_analysis'
    os.makedirs(output_dir, exist_ok=True)
    fig.savefig(os.path.join(output_dir, 'Electric_Noise_FFT_Comparison.png'), dpi=600, bbox_inches='tight')

    # plt.figure(figsize=(10, 6))
    # plt.loglog(df_original['Frequency_Hz'], df_original['Data_2_1 interleaved ch2'], label='OPO + SHG + Highpass + 20 dB Attenuator @800 nm, 2.09 urad^2/rtHz', linewidth=1)
    # plt.loglog(df_bandpass['Frequency_Hz'], df_bandpass['Data_2_1 interleaved ch2'], label='OPO + SHG + Highpass + Bandpass @800 nm, 2.02 urad^2/rtHz', linewidth=1)
    # plt.loglog(df_dark['Frequency_Hz'], df_dark['Data_2_1 interleaved ch2'], label='No incident light', linewidth=1)
    # plt.loglog(df_blocked['Frequency_Hz'], df_blocked['Data_2_1 interleaved ch2'], label='BPDs Blocked', linewidth=1)
    # plt.xlabel('Frequency_Hz')
    # plt.ylabel('Amplitude')
    # plt.title('FFT Analysis')
    # plt.legend()
    # plt.grid(True, 'both', axis='both')
    # plt.minorticks_on
    # plt.show()

def hardware_test():
    AT1_BP1 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_151904/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    BP1 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_152519/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    AT2_BP1 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_153215/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    AT1_BP2 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_154237/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    BP2 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_155502/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    AT2_BP2 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260615_160323/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    df_AT1_BP1 = pd.read_csv(AT1_BP1)
    df_BP1 = pd.read_csv(BP1)
    df_AT2_BP1 = pd.read_csv(AT2_BP1)
    df_AT1_BP2 = pd.read_csv(AT1_BP2)
    df_BP2 = pd.read_csv(BP2)
    df_AT2_BP2 = pd.read_csv(AT2_BP2)

    fig, axes = plt.subplots(1, 1, figsize=(16, 9))
    # axes.loglog(df_AT1_BP1['Frequency_Hz'], df_AT1_BP1['Data_1_1 interleaved ch1'], label='AT1_BP1', linewidth=1)
    # axes.loglog(df_BP1['Frequency_Hz'], df_BP1['Data_1_1 interleaved ch1'], label='BP1', linewidth=1)
    # axes.loglog(df_AT2_BP1['Frequency_Hz'], df_AT2_BP1['Data_1_1 interleaved ch1'], label='AT2_BP1', linewidth=1)
    # axes.loglog(df_AT1_BP2['Frequency_Hz'], df_AT1_BP2['Data_1_1 interleaved ch1'], label='AT1_BP2', linewidth=1)
    
    axes.loglog(df_BP2['Frequency_Hz'], df_BP2['Data_1_1 interleaved ch1'], label='BP2', linewidth=1)
    axes.loglog(df_AT2_BP2['Frequency_Hz'], df_AT2_BP2['Data_1_1 interleaved ch1'], label='AT2_BP2', linewidth=1)
    axes.set_xlabel('Frequency_Hz')
    axes.set_ylabel('PSD (urad^2/rtHz)')
    axes.set_title('Data_1_1 interleaved ch1')
    axes.legend()
    axes.grid(True, 'both', axis='both')
    plt.show()

def fft_analysis():
    # BPD_off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_151246/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    BPD_off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_145821/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    
    highpass_bandpass_off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_162124/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    highpass_attenuator_off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_162755/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    highpass_bandpass_attenuator_off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_161343/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    Highpass_bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_172219/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    Highpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_171152/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    Highpass_bandpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_172918/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    # light_on_highpass_bandpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_164832/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    # light_on_highpass_bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_165555/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    # light_on_highpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_170542/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    
    df_BPD_off = pd.read_csv(BPD_off)

    df_highpass_bandpass_off = pd.read_csv(highpass_bandpass_off)
    df_highpass_attenuator_off = pd.read_csv(highpass_attenuator_off)
    df_highpass_bandpass_attenuator_off = pd.read_csv(highpass_bandpass_attenuator_off)

    df_Highpass_bandpass = pd.read_csv(Highpass_bandpass)
    df_Highpass_attenuator = pd.read_csv(Highpass_attenuator)
    df_Highpass_bandpass_attenuator = pd.read_csv(Highpass_bandpass_attenuator)

    # df_light_on_highpass_bandpass_attenuator = pd.read_csv(light_on_highpass_bandpass_attenuator)
    # df_light_on_highpass_bandpass = pd.read_csv(light_on_highpass_bandpass)
    # df_light_on_highpass_attenuator = pd.read_csv(light_on_highpass_attenuator)


    fig, axes = plt.subplots(2, 1, figsize=(16, 9), sharex=True)
    ax1, ax2 = axes
    
    # ax1.loglog(df_light_on_highpass_bandpass_attenuator['Frequency_Hz'], df_light_on_highpass_bandpass_attenuator['Data_1_1 interleaved ch1'], label='light_on_highpass_bandpass_attenuator', linewidth=1)
    ax1.loglog(df_highpass_bandpass_attenuator_off['Frequency_Hz'], df_highpass_bandpass_attenuator_off['Data_1_1 interleaved ch1'], label='highpass_bandpass_attenuator_off', linewidth=1)
    # ax1.loglog(df_Highpass_bandpass_attenuator['Frequency_Hz'], df_Highpass_bandpass_attenuator['Data_1_1 interleaved ch1'], label='highpass_bandpass_attenuator_blocked', linewidth=1)
    
    ax1.loglog(df_BPD_off['Frequency_Hz'], df_BPD_off['Data_1_1 interleaved ch1'], label='Terminator', linewidth=1)

    # ax1.loglog(df_light_on_highpass_attenuator['Frequency_Hz'], df_light_on_highpass_attenuator['Data_1_1 interleaved ch1'], label='highpass_attenuator', linewidth=1)
    # ax1.loglog(df_light_on_highpass_bandpass['Frequency_Hz'], df_light_on_highpass_bandpass['Data_1_1 interleaved ch1'], label='highpass_bandpass', linewidth=1)
    # ax1.loglog(df_light_on_highpass_bandpass_attenuator['Frequency_Hz'], df_light_on_highpass_bandpass_attenuator['Data_1_1 interleaved ch1'], label='highpass_bandpass_attenuator', linewidth=1)
    # ax1.loglog(df_BPD_off['Frequency_Hz'], df_highpass_bandpass_attenuator_off['Data_1_1 interleaved ch1'], label='BPDs Off', linewidth=1)
    
    
    # ax2.loglog(df_light_on_highpass_attenuator['Frequency_Hz'], df_light_on_highpass_attenuator['Data_2_1 interleaved ch2'], label='highpass_attenuator', linewidth=1)
    # ax2.loglog(df_light_on_highpass_bandpass['Frequency_Hz'], df_light_on_highpass_bandpass['Data_2_1 interleaved ch2'], label='highpass_bandpass', linewidth=1)
    # ax2.loglog(df_light_on_highpass_bandpass_attenuator['Frequency_Hz'], df_light_on_highpass_bandpass_attenuator['Data_2_1 interleaved ch2'], label='highpass_bandpass_attenuator', linewidth=1)
    # ax2.loglog(df_BPD_off['Frequency_Hz'], df_highpass_bandpass_attenuator_off['Data_2_1 interleaved ch2'], label='BPDs Off', linewidth=1)
    
    
    # ax1.loglog(df_light_on_highpass_attenuator['Frequency_Hz'], df_light_on_highpass_attenuator['Data_1_1 interleaved ch1'] / df_BPD_off['Data_1_1 interleaved ch1'], label='highpass_attenuator', linewidth=1)
    # ax1.loglog(df_light_on_highpass_bandpass['Frequency_Hz'], df_light_on_highpass_bandpass['Data_1_1 interleaved ch1'] / df_BPD_off['Data_1_1 interleaved ch1'], label='highpass_bandpass', linewidth=1)
    # ax1.loglog(df_light_on_highpass_bandpass_attenuator['Frequency_Hz'], df_light_on_highpass_bandpass_attenuator['Data_1_1 interleaved ch1'] / df_BPD_off['Data_1_1 interleaved ch1'], label='highpass_bandpass_attenuator', linewidth=1)
   
    # ax2.loglog(df_light_on_highpass_attenuator['Frequency_Hz'], df_light_on_highpass_attenuator['Data_2_1 interleaved ch2'] / df_BPD_off['Data_2_1 interleaved ch2'], label='highpass_attenuator', linewidth=1)
    # ax2.loglog(df_light_on_highpass_bandpass['Frequency_Hz'], df_light_on_highpass_bandpass['Data_2_1 interleaved ch2'] / df_BPD_off['Data_2_1 interleaved ch2'], label='highpass_bandpass', linewidth=1)
    # ax2.loglog(df_light_on_highpass_bandpass_attenuator['Frequency_Hz'], df_light_on_highpass_bandpass_attenuator['Data_2_1 interleaved ch2'] / df_BPD_off['Data_2_1 interleaved ch2'], label='highpass_bandpass_attenuator', linewidth=1)    

    # ax1.loglog(df_light_on_highpass_attenuator['Frequency_Hz'], df_light_on_highpass_attenuator['Data_1_1 interleaved ch1'] / df_highpass_attenuator_off['Data_1_1 interleaved ch1'], label='highpass_attenuator', linewidth=1)
    # ax1.loglog(df_light_on_highpass_bandpass['Frequency_Hz'], df_light_on_highpass_bandpass['Data_1_1 interleaved ch1'] / df_highpass_bandpass_off['Data_1_1 interleaved ch1'], label='highpass_bandpass', linewidth=1)
    # ax1.loglog(df_light_on_highpass_bandpass_attenuator['Frequency_Hz'], df_light_on_highpass_bandpass_attenuator['Data_1_1 interleaved ch1'] / df_highpass_bandpass_attenuator_off['Data_1_1 interleaved ch1'], label='highpass_bandpass_attenuator', linewidth=1)
   
    # ax2.loglog(df_light_on_highpass_attenuator['Frequency_Hz'], df_light_on_highpass_attenuator['Data_2_1 interleaved ch2'] / df_highpass_attenuator_off['Data_2_1 interleaved ch2'], label='highpass_attenuator', linewidth=1)
    # ax2.loglog(df_light_on_highpass_bandpass['Frequency_Hz'], df_light_on_highpass_bandpass['Data_2_1 interleaved ch2'] / df_highpass_bandpass_off['Data_2_1 interleaved ch2'], label='highpass_bandpass', linewidth=1)
    # ax2.loglog(df_light_on_highpass_bandpass_attenuator['Frequency_Hz'], df_light_on_highpass_bandpass_attenuator['Data_2_1 interleaved ch2'] / df_highpass_bandpass_attenuator_off['Data_2_1 interleaved ch2'], label='highpass_bandpass_attenuator', linewidth=1)    


    ax1.set_xlabel('Frequency (Hz)')
    ax1.set_ylabel('PSD (urad^2/rtHz)')
    ax1.set_title('Data_1_1 interleaved ch1')
    ax1.legend()
    ax1.grid(True, 'both', axis='both')

    # ax2.loglog(df_light_on_highpass_bandpass_attenuator['Frequency_Hz'], df_light_on_highpass_bandpass_attenuator['Data_2_1 interleaved ch2'], label='light_on_highpass_bandpass_attenuator', linewidth=1)
    ax2.loglog(df_highpass_bandpass_attenuator_off['Frequency_Hz'], df_highpass_bandpass_attenuator_off['Data_2_1 interleaved ch2'], label='highpass_bandpass_attenuator_off', linewidth=1)
    ax2.loglog(df_Highpass_bandpass_attenuator['Frequency_Hz'], df_Highpass_bandpass_attenuator['Data_2_1 interleaved ch2'], label='highpass_bandpass_attenuator_blocked', linewidth=1)
    ax2.loglog(df_BPD_off['Frequency_Hz'], df_BPD_off['Data_2_1 interleaved ch2'], label='Null', linewidth=1)

    ax2.set_xlabel('Frequency (Hz)')
    ax2.set_ylabel('PSD (urad^2/rtHz)')
    ax2.set_title('Data_2_1 interleaved ch2')
    ax2.legend()
    ax2.grid(True, 'both', axis='both')

    fig.suptitle('Low Frequency FFT Log-Log Comparison by Channel')
    plt.tight_layout()
    output_dir = 'D:/Quantum Squeezing Project/DataFiles/FFT_analysis'
    os.makedirs(output_dir, exist_ok=True)
    fig.savefig(os.path.join(output_dir, 'Low_Frequency_FFT_Comparison.png'), dpi=600, bbox_inches='tight')

    plt.show()

def BPD_off_analysis():
    # BP1AT2/BP2AT1
    # highpass_bandpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_145758/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    # highpass_bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_150612/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    # highpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_152650/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    # null = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_151246/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    # BP1AT1/BP2AT2
    # highpass_bandpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_160137/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    # highpass_bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_150612/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    # highpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_152650/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    # null = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_151246/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    # BP2AT2/BP1AT1
    BPD_off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_145821/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    
    highpass_bandpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_161343/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    highpass_bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_162124/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    highpass_attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_162755/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    null = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_151246/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    
    df_highpass_bandpass_attenuator = pd.read_csv(highpass_bandpass_attenuator)
    df_highpass_bandpass = pd.read_csv(highpass_bandpass)
    df_highpass_attenuator = pd.read_csv(highpass_attenuator)
    df_null = pd.read_csv(null)
    df_BPD_off = pd.read_csv(BPD_off)

    fig, axes = plt.subplots(2, 1, figsize=(16, 9), sharex=True)
    # ax1, ax2, ax3, ax4, ax5, ax6 = axes
    ax1, ax2 = axes

    # ax1.loglog(df_highpass_bandpass_attenuator['Frequency_Hz'], df_highpass_bandpass_attenuator['Data_1_1 interleaved ch1'], label='highpass_bandpass_attenuator', linewidth=1)
    # ax1.loglog(df_null['Frequency_Hz'], df_null['Data_1_1 interleaved ch1'], label='null', linewidth=1)

    # ax2.loglog(df_highpass_bandpass_attenuator['Frequency_Hz'], df_highpass_bandpass_attenuator['Data_2_1 interleaved ch2'], label='highpass_bandpass_attenuator', linewidth=1)
    # ax2.loglog(df_null['Frequency_Hz'], df_null['Data_2_1 interleaved ch2'], label='null', linewidth=1)

    # ax1.loglog(df_highpass_bandpass['Frequency_Hz'], df_highpass_bandpass['Data_1_1 interleaved ch1'], label='highpass_bandpass', linewidth=1)
    # ax1.loglog(df_null['Frequency_Hz'], df_null['Data_1_1 interleaved ch1'], label='null', linewidth=1)

    # ax2.loglog(df_highpass_bandpass['Frequency_Hz'], df_highpass_bandpass['Data_2_1 interleaved ch2'], label='highpass_bandpass', linewidth=1)
    # ax2.loglog(df_null['Frequency_Hz'], df_null['Data_2_1 interleaved ch2'], label='null', linewidth=1)

    ax1.loglog(df_highpass_attenuator['Frequency_Hz'], df_highpass_attenuator['Data_1_1 interleaved ch1'], label='highpass_attenuator', linewidth=1)
    ax1.loglog(df_highpass_bandpass['Frequency_Hz'], df_highpass_bandpass['Data_1_1 interleaved ch1'], label='highpass_bandpass', linewidth=1)
    ax1.loglog(df_highpass_bandpass_attenuator['Frequency_Hz'], df_highpass_bandpass_attenuator['Data_1_1 interleaved ch1'], label='highpass_bandpass_attenuator', linewidth=1)
    ax1.loglog(df_BPD_off['Frequency_Hz'], df_BPD_off['Data_1_1 interleaved ch1'], label='BPD Off', linewidth=1)

    ax2.loglog(df_highpass_attenuator['Frequency_Hz'], df_highpass_attenuator['Data_2_1 interleaved ch2'], label='highpass_attenuator', linewidth=1)
    ax2.loglog(df_highpass_bandpass['Frequency_Hz'], df_highpass_bandpass['Data_2_1 interleaved ch2'], label='highpass_bandpass', linewidth=1)
    ax2.loglog(df_highpass_bandpass_attenuator['Frequency_Hz'], df_highpass_bandpass_attenuator['Data_2_1 interleaved ch2'], label='highpass_bandpass_attenuator', linewidth=1)
    ax2.loglog(df_BPD_off['Frequency_Hz'], df_BPD_off['Data_2_1 interleaved ch2'], label='BPD Off', linewidth=1)

    ax1.set_xlabel('Frequency (Hz)')
    ax1.set_ylabel('PSD (urad^2/rtHz)')
    ax1.set_title('Data_1_1 interleaved ch1')
    ax1.legend()
    ax1.grid(True, 'both', axis='both')

    ax2.set_xlabel('Frequency (Hz)')
    ax2.set_ylabel('PSD (urad^2/rtHz)')
    ax2.set_title('Data_2_1 interleaved ch2')
    ax2.legend()
    ax2.grid(True, 'both', axis='both')

    plt.tight_layout()
    output_dir = 'D:/Quantum Squeezing Project/DataFiles/FFT_analysis'
    os.makedirs(output_dir, exist_ok=True)
    fig.savefig(os.path.join(output_dir, 'BPD_Off_Analysis_BPD_Off.png'), dpi=600, bbox_inches='tight')
    # plt.show()

def filter_test():
    # # terminator
    BPD_Off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_151246/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    highpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_142936/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_143821/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    attenuator = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_141913/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    # BPD Off
    BPD_Off_2 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_145821/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    highpass_2 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_155526/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    bandpass_2 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_153542/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    attenuator_2 = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_150903/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    df_BPD_Off = pd.read_csv(BPD_Off)
    df_highpass = pd.read_csv(highpass)
    df_bandpass = pd.read_csv(bandpass)
    df_attenuator = pd.read_csv(attenuator)

    df_BPD_Off_2 = pd.read_csv(BPD_Off_2)
    df_highpass_2 = pd.read_csv(highpass_2)
    df_bandpass_2 = pd.read_csv(bandpass_2)
    df_attenuator_2 = pd.read_csv(attenuator_2)
    
    fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(16, 9), sharex=True, sharey=True)

    # ax1.loglog(df_highpass['Frequency_Hz'], df_highpass['Data_1_1 interleaved ch1'], label='Highpass terminator', linewidth=1)
    # ax1.loglog(df_bandpass['Frequency_Hz'], df_bandpass['Data_1_1 interleaved ch1'], label='Bandpass terminator', linewidth=1)
    # ax1.loglog(df_attenuator['Frequency_Hz'], df_attenuator['Data_1_1 interleaved ch1'], label='Attenuator terminator', linewidth=1)
    # ax1.loglog(df_BPD_Off['Frequency_Hz'], df_BPD_Off['Data_1_1 interleaved ch1'], label='BPD Off terminator', linewidth=1)

    # ax1.loglog(df_highpass_2['Frequency_Hz'], df_highpass_2['Data_1_1 interleaved ch1'], label='Highpass', linewidth=1)
    # ax1.loglog(df_bandpass_2['Frequency_Hz'], df_bandpass_2['Data_1_1 interleaved ch1'], label='Bandpass', linewidth=1)
    ax1.loglog(df_attenuator_2['Frequency_Hz'], df_attenuator_2['Data_1_1 interleaved ch1'], label='Attenuator', linewidth=1)
    ax1.loglog(df_BPD_Off_2['Frequency_Hz'], df_BPD_Off_2['Data_1_1 interleaved ch1'], label='BPD Off', linewidth=1)

    # ax2.loglog(df_highpass['Frequency_Hz'], df_highpass['Data_2_1 interleaved ch2'], label='Highpass terminator', linewidth=1)
    # ax2.loglog(df_bandpass['Frequency_Hz'], df_bandpass['Data_2_1 interleaved ch2'], label='Bandpass terminator', linewidth=1)
    # ax2.loglog(df_attenuator['Frequency_Hz'], df_attenuator['Data_2_1 interleaved ch2'], label='Attenuator terminator', linewidth=1)
    # ax2.loglog(df_BPD_Off['Frequency_Hz'], df_BPD_Off['Data_2_1 interleaved ch2'], label='BPD Off terminator', linewidth=1)

    # ax2.loglog(df_highpass_2['Frequency_Hz'], df_highpass_2['Data_2_1 interleaved ch2'], label='Highpass', linewidth=1)
    # ax2.loglog(df_bandpass_2['Frequency_Hz'], df_bandpass_2['Data_2_1 interleaved ch2'], label='Bandpass', linewidth=1)
    ax2.loglog(df_attenuator_2['Frequency_Hz'], df_attenuator_2['Data_2_1 interleaved ch2'], label='Attenuator', linewidth=1)
    ax2.loglog(df_BPD_Off_2['Frequency_Hz'], df_BPD_Off_2['Data_2_1 interleaved ch2'], label='BPD Off', linewidth=1)

    ax1.set_xlabel('Frequency (Hz)')
    ax1.set_ylabel('PSD (urad^2/rtHz)')
    ax1.set_title('Data_1_1 interleaved ch1')
    ax1.legend()
    ax1.grid(True, 'both', axis='both')

    ax2.set_xlabel('Frequency (Hz)')
    ax2.set_ylabel('PSD (urad^2/rtHz)')
    ax2.set_title('Data_2_1 interleaved ch2')
    ax2.legend()
    ax2.grid(True, 'both', axis='both')

    plt.tight_layout()
    output_dir = 'D:/Quantum Squeezing Project/DataFiles/FFT_analysis'
    os.makedirs(output_dir, exist_ok=True)
    fig.savefig(os.path.join(output_dir, 'attenuator_check.png'), dpi=600, bbox_inches='tight')
    plt.show()

def filter_flipped_test():
    null = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_151246/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    BPD_Off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_160906/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    BPD_Blocked = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_161656/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    attenuator_highpass_bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_162936/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    df_null = pd.read_csv(null)
    df_BPD_Off = pd.read_csv(BPD_Off)
    df_BPD_Blocked = pd.read_csv(BPD_Blocked)
    df_attenuator_highpass_bandpass = pd.read_csv(attenuator_highpass_bandpass)

    fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(16, 9), sharex=True, sharey=True)
    ax1.loglog(df_attenuator_highpass_bandpass['Frequency_Hz'], df_attenuator_highpass_bandpass['Data_1_1 interleaved ch1'], label='Attenuator_Highpass_Bandpass_(with_light)', linewidth=1)
    ax1.loglog(df_BPD_Blocked['Frequency_Hz'], df_BPD_Blocked['Data_1_1 interleaved ch1'], label='BPD Blocked', linewidth=1)
    ax1.loglog(df_BPD_Off['Frequency_Hz'], df_BPD_Off['Data_1_1 interleaved ch1'], label='BPD Off', linewidth=1)
    ax1.loglog(df_null['Frequency_Hz'], df_null['Data_1_1 interleaved ch1'], label='Null', linewidth=1)

    ax2.loglog(df_attenuator_highpass_bandpass['Frequency_Hz'], df_attenuator_highpass_bandpass['Data_2_1 interleaved ch2'], label='Attenuator_Highpass_Bandpass_(with_light)', linewidth=1)
    ax2.loglog(df_BPD_Blocked['Frequency_Hz'], df_BPD_Blocked['Data_2_1 interleaved ch2'], label='BPD Blocked', linewidth=1)
    ax2.loglog(df_BPD_Off['Frequency_Hz'], df_BPD_Off['Data_2_1 interleaved ch2'], label='BPD Off', linewidth=1)
    ax2.loglog(df_null['Frequency_Hz'], df_null['Data_2_1 interleaved ch2'], label='Null', linewidth=1)

    ax1.set_xlabel('Frequency (Hz)')
    ax1.set_ylabel('PSD (urad^2/rtHz)')
    ax1.set_title('Data_1_1 interleaved ch1')
    ax1.legend()
    ax1.grid(True, 'both', axis='both')

    ax2.set_xlabel('Frequency (Hz)')
    ax2.set_ylabel('PSD (urad^2/rtHz)')
    ax2.set_title('Data_2_1 interleaved ch2')
    ax2.legend()
    ax2.grid(True, 'both', axis='both')

    plt.tight_layout()
    output_dir = 'D:/Quantum Squeezing Project/DataFiles/FFT_analysis'
    os.makedirs(output_dir, exist_ok=True)
    fig.savefig(os.path.join(output_dir, 'flipped_filter_test(attenuator_highpass_bandpass).png'), dpi=600, bbox_inches='tight')
    plt.show()

def BPD_off_cross_test():
    BPD_null = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260617_151246/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    BPD_Off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_160906/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    BPD_Blocked = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_161656/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    attenuator_highpass_bandpass = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260618_162936/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    df_BPD_Off = pd.read_csv(BPD_Off)
    df_BPD_Blocked = pd.read_csv(BPD_Blocked)
    df_attenuator_highpass_bandpass = pd.read_csv(attenuator_highpass_bandpass)

    fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(16, 9))
    ax1.loglog(df_attenuator_highpass_bandpass['Frequency_Hz'], df_attenuator_highpass_bandpass['Data_1_1 interleaved ch1'], label='Attenuator/Highpass/Bandpass', linewidth=1)
    ax1.loglog(df_BPD_Blocked['Frequency_Hz'], df_BPD_Blocked['Data_1_1 interleaved ch1'], label='BPD Blocked', linewidth=1)
    ax1.loglog(df_BPD_Off['Frequency_Hz'], df_BPD_Off['Data_1_1 interleaved ch1'], label='BPD Off', linewidth=1)

    ax2.loglog(df_attenuator_highpass_bandpass['Frequency_Hz'], df_attenuator_highpass_bandpass['Data_2_1 interleaved ch2'], label='Attenuator/Highpass/Bandpass', linewidth=1)
    ax2.loglog(df_BPD_Blocked['Frequency_Hz'], df_BPD_Blocked['Data_2_1 interleaved ch2'], label='BPD Blocked', linewidth=1)
    ax2.loglog(df_BPD_Off['Frequency_Hz'], df_BPD_Off['Data_2_1 interleaved ch2'], label='BPD Off', linewidth=1)

    ax1.set_xlabel('Frequency (Hz)')
    ax1.set_ylabel('PSD (urad^2/rtHz)')
    ax1.set_title('Data_1_1 interleaved ch1')
    ax1.legend()
    ax1.grid(True, 'both', axis='both')

    ax2.set_xlabel('Frequency (Hz)')
    ax2.set_ylabel('PSD (urad^2/rtHz)')
    ax2.set_title('Data_2_1 interleaved ch2')
    ax2.legend()
    ax2.grid(True, 'both', axis='both')

    plt.tight_layout()
    output_dir = 'D:/Quantum Squeezing Project/DataFiles/FFT_analysis'
    os.makedirs(output_dir, exist_ok=True)
    fig.savefig(os.path.join(output_dir, 'BPD_off_cross_test.png'), dpi=600, bbox_inches='tight')
    plt.show()

def two_Bandpass_test():
    Off = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260629_105750/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    Blocked = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260629_110724/fft_analysis/interleaved_fft_low_frequency_loglog.csv')
    light = os.path.abspath('D:/Quantum Squeezing Project/DataFiles/20260629_153455/fft_analysis/interleaved_fft_low_frequency_loglog.csv')

    df_Off = pd.read_csv(Off)
    df_Blocked = pd.read_csv(Blocked)
    df_light = pd.read_csv(light)

    fig, axes = plt.subplots(2, 1, figsize=(16, 9))
    ax1, ax2 = axes

    ax1.loglog(df_light['Frequency_Hz'], df_light['Data_1_1 interleaved ch1'], label='Light', linewidth=1)
    ax1.loglog(df_Blocked['Frequency_Hz'], df_Blocked['Data_1_1 interleaved ch1'], label='Blocked', linewidth=1)
    ax1.loglog(df_Off['Frequency_Hz'], df_Off['Data_1_1 interleaved ch1'], label='Off', linewidth=1)
    
    ax2.loglog(df_light['Frequency_Hz'], df_light['Data_2_1 interleaved ch2'], label='Light', linewidth=1)
    ax2.loglog(df_Blocked['Frequency_Hz'], df_Blocked['Data_2_1 interleaved ch2'], label='Blocked', linewidth=1)
    ax2.loglog(df_Off['Frequency_Hz'], df_Off['Data_2_1 interleaved ch2'], label='Off', linewidth=1)

    ax1.legend()
    ax2.legend()
    ax1.set_xlabel('Frequency (Hz)')
    ax1.set_ylabel('PSD (urad^2/rtHz)')
    ax1.set_title('Data_1_1 interleaved ch1')
    ax1.grid(True, 'both', axis='both')

    ax2.set_xlabel('Frequency (Hz)')
    ax2.set_ylabel('PSD (urad^2/rtHz)')
    ax2.set_title('Data_2_1 interleaved ch2')
    ax2.grid(True, 'both', axis='both')

    plt.tight_layout()
    plt.show()

def diagonal_tail_off_FFT(
    run=r"D:\Quantum Squeezing Project\DataFiles\20260712_173813\cm.bin",
    pair=((5, 5), (8, 8)),
    frame_dt_s=0.025,
    save_results=True,
    show_plot=True,
):
    """Extract and Fourier transform a user-selected matrix-element pair.

    Matrix coordinates are one-based, as used in the experiment notation.  The
    binary file contains 64 little-endian float64 values per 8x8 frame, stored
    row by row.  Set ``pair=((row1, col1), (row2, col2))`` to choose the two
    entries.  Frame 1 in the output is the first 64 values.
    """
    run = os.path.abspath(run)
    if not os.path.isfile(run):
        raise FileNotFoundError(f"Covariance-matrix file not found: {run}")
    if frame_dt_s <= 0:
        raise ValueError("frame_dt_s must be positive")
    if (
        len(pair) != 2
        or any(len(coordinate) != 2 for coordinate in pair)
        or any(not 1 <= index <= 8 for coordinate in pair for index in coordinate)
    ):
        raise ValueError("pair must be ((row1, col1), (row2, col2)), using 1-8")

    values = np.fromfile(run, dtype="<f8")
    values_per_frame = 8 * 8
    remainder = values.size % values_per_frame
    if remainder:
        raise ValueError(
            f"{run} contains {values.size} float64 values, which is not a "
            f"whole number of 8x8 frames ({remainder} trailing values)."
        )
    if values.size == 0:
        raise ValueError(f"No frames found in {run}")

    frames = values.reshape(-1, 8, 8)
    (first_row, first_col), (second_row, second_col) = pair
    first_values = frames[:, first_row - 1, first_col - 1]
    second_values = frames[:, second_row - 1, second_col - 1]
    difference = first_values - second_values
    pair_label = (
        f"CM({first_row},{first_col}) - "
        f"CM({second_row},{second_col})"
    )
    frame_number = np.arange(1, frames.shape[0] + 1)
    time_s = (frame_number - 1) * frame_dt_s

    extracted = pd.DataFrame({
        "Frame": frame_number,
        "Time_s": time_s,
        f"CM_{first_row}_{first_col}": first_values,
        f"CM_{second_row}_{second_col}": second_values,
        "Critical_pair_difference": difference,
    })

    # Transform the complete difference signal, including its mean (DC term).
    fft_values = np.fft.rfft(difference)
    frequency_hz = np.fft.rfftfreq(difference.size, d=frame_dt_s)
    amplitude = np.abs(fft_values) / difference.size
    if difference.size > 1:
        amplitude[1:-1 if difference.size % 2 == 0 else None] *= 2.0

    spectrum = pd.DataFrame({
        "Frequency_Hz": frequency_hz,
        "Amplitude": amplitude,
        "Power": amplitude ** 2,
    })

    fig, (ax_time, ax_fft) = plt.subplots(2, 1, figsize=(12, 8))
    fig.suptitle(f"Tail-Off Pair: {pair_label}")
    ax_time.plot(frame_number, difference, linewidth=1)
    ax_time.set_xlabel("Frame (one-based)")
    ax_time.set_ylabel(pair_label)
    ax_time.grid(True)

    ax_fft.plot(frequency_hz, amplitude, linewidth=1)
    ax_fft.set_xlabel("Frequency (Hz)")
    ax_fft.set_ylabel("Single-sided amplitude")
    ax_fft.grid(True)
    fig.tight_layout()

    if save_results:
        output_dir = os.path.join(os.path.dirname(run), "fft_analysis")
        os.makedirs(output_dir, exist_ok=True)
        extracted.to_csv(
            os.path.join(output_dir, "diagonal_tail_off_by_frame.csv"), index=False
        )
        spectrum.to_csv(
            os.path.join(output_dir, "diagonal_tail_off_fft.csv"), index=False
        )
        fig.savefig(
            os.path.join(output_dir, "diagonal_tail_off_fft.png"),
            dpi=300,
            bbox_inches="tight",
        )

    if show_plot:
        plt.show()
    else:
        plt.close(fig)

    return extracted, spectrum



if __name__ == "__main__":
    # plot_fft_comparison()
    # hardware_test()
    # fft_analysis()
    # BPD_off_analysis()
    # filter_test()
    # filter_flipped_test()
    # two_Bandpass_test()
    diagonal_tail_off_FFT(
        run=r"D:\Quantum Squeezing Project\DataFiles\20260716_002703\cm.bin",
        pair=((1, 4), (5, 8)),  # Enter the two matrix elements here.
        frame_dt_s=0.025,
        save_results=True,
        show_plot=True,
    )
